using System.Buffers;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Threading.Channels;
using EMCL.Core.Codecs;
using EMCL.Core.Values;
using EMCL.Objects.Association;
using EMCL.Protocol.Axdr;

namespace EMCL.Client.Sessions;

/// <summary>A single-use, incrementally decoded Association LN object_list.</summary>
/// <remarks>TotalCount is the number of entries advertised by the meter, including duplicate entries.
/// Dispose the stream when enumeration is stopped early. An incomplete GET faults the owning session.</remarks>
public sealed class AssociationObjectStream : IAsyncEnumerable<AssociationObjectEntry>, IAsyncDisposable
{
    private readonly ChannelReader<byte[]> chunks;
    private readonly Task producer;
    private readonly CancellationTokenSource source;
    private readonly AssociationImporter importer;
    private readonly byte associationVersion;
    private readonly ProtocolLimits limits;
    private readonly AxdrCodec codec;
    private readonly List<byte> pending = [];
    private int enumerationStarted;
    private int disposed;
    private int currentCount;

    internal AssociationObjectStream(ChannelReader<byte[]> chunks, Task producer, CancellationTokenSource source,
        AssociationImporter importer, byte associationVersion, ProtocolLimits limits)
    {
        this.chunks = chunks;
        this.producer = producer;
        this.source = source;
        this.importer = importer;
        this.associationVersion = associationVersion;
        this.limits = limits;
        codec = new(limits);
    }

    /// <summary>Gets the array element count advertised by the meter, including duplicates.</summary>
    public int TotalCount { get; private set; }

    /// <summary>Gets the number of fully decoded entries yielded so far.</summary>
    public int CurrentCount => Volatile.Read(ref currentCount);

    internal async ValueTask InitializeAsync(CancellationToken cancellationToken)
    {
        while (!TryReadHeader())
        {
            if (!await ReadMoreAsync(cancellationToken).ConfigureAwait(false))
            {
                throw new AssociationImportException("Incomplete object_list array header.");
            }
        }
    }

    /// <inheritdoc />
    public IAsyncEnumerator<AssociationObjectEntry> GetAsyncEnumerator(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        if (Interlocked.Exchange(ref enumerationStarted, 1) != 0)
        {
            throw new InvalidOperationException("The object_list stream can be enumerated only once.");
        }

        return EnumerateAsync(cancellationToken).GetAsyncEnumerator(cancellationToken);
    }

    private async IAsyncEnumerable<AssociationObjectEntry> EnumerateAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        try
        {
            while (CurrentCount < TotalCount)
            {
                var entry = await ReadEntryAsync(cancellationToken).ConfigureAwait(false);
                Interlocked.Increment(ref currentCount);
                yield return entry;
            }

            // The declared count is insufficient if the transfer contains trailing data.
            if (pending.Count != 0)
            {
                throw new AssociationImportException("Trailing data after object_list entries.");
            }

            while (await ReadMoreAsync(cancellationToken).ConfigureAwait(false))
            {
                if (pending.Count != 0)
                {
                    throw new AssociationImportException("Trailing data after object_list entries.");
                }
            }

            if (pending.Count != 0)
            {
                throw new AssociationImportException("Trailing data after object_list entries.");
            }
        }
        finally
        {
            await DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        await source.CancelAsync().ConfigureAwait(false);
        await producer.ConfigureAwait(false);
        source.Dispose();
    }

    private async ValueTask<AssociationObjectEntry> ReadEntryAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            if (pending.Count != 0)
            {
                var decoded = codec.Decode(new ReadOnlySequence<byte>(pending.ToArray()));
                if (decoded.Status == DecodeStatus.Done)
                {
                    try
                    {
                        var value = decoded.Value!;
                        var parsed = importer.ParseEntry(value, associationVersion);
                        pending.RemoveRange(0, checked((int)decoded.Consumed));
                        return new(value, parsed.Descriptor, parsed.Name, parsed.Rights);
                    }
                    catch (Exception error) when (error is ArgumentException or FormatException or InvalidOperationException or OverflowException)
                    {
                        throw new AssociationImportException($"Invalid object_list entry: {error.Message}", error);
                    }
                }

                if (decoded.Status != DecodeStatus.NeedMoreData)
                {
                    throw new AssociationImportException($"Invalid object_list entry: {decoded.Status}; {decoded.Error}.");
                }
            }

            if (!await ReadMoreAsync(cancellationToken).ConfigureAwait(false))
            {
                throw new AssociationImportException("Incomplete object_list entry.");
            }
        }
    }

    private bool TryReadHeader()
    {
        if (pending.Count == 0)
        {
            return false;
        }

        if (pending[0] != (byte)DlmsType.Array)
        {
            throw new AssociationImportException("object_list must be an A-XDR array.");
        }

        if (pending.Count < 2)
        {
            return false;
        }

        byte first = pending[1];
        int lengthBytes = first < 0x80 ? 0 : first & 0x7F;
        if (first >= 0x80 && lengthBytes is 0 or > 4)
        {
            throw new AssociationImportException("Invalid object_list array length.");
        }

        if (pending.Count < 2 + lengthBytes)
        {
            return false;
        }

        uint count = first < 0x80 ? first : 0u;
        for (int i = 0; i < lengthBytes; i++)
        {
            count = (count << 8) | pending[2 + i];
        }

        if (count > limits.MaxCollectionItems)
        {
            throw new AssociationImportException("object_list count exceeds the collection limit.");
        }

        TotalCount = (int)count;
        pending.RemoveRange(0, 2 + lengthBytes);
        return true;
    }

    private async ValueTask<bool> ReadMoreAsync(CancellationToken cancellationToken)
    {
        try
        {
            var chunk = await chunks.ReadAsync(cancellationToken).ConfigureAwait(false);
            if (chunk.Length > limits.MaxMessageBytes - pending.Count)
            {
                throw new AssociationImportException("object_list exceeds the message limit.");
            }

            pending.AddRange(chunk);
            return true;
        }
        catch (ChannelClosedException error) when (error.InnerException != null)
        {
            ExceptionDispatchInfo.Capture(error.InnerException).Throw();
            throw;
        }
        catch (ChannelClosedException)
        {
            return false;
        }
    }
}
