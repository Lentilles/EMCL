using System.Buffers;
using System.Runtime.CompilerServices;
using EMCL.Client.Internal;
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
    private readonly GetDataStream data;
    private readonly AssociationImporter importer;
    private readonly byte associationVersion;
    private readonly ProtocolLimits limits;
    private readonly AxdrCodec codec;
    private int enumerationStarted;
    private int disposed;
    private int currentCount;

    internal AssociationObjectStream(GetDataStream data, AssociationImporter importer, byte associationVersion,
        ProtocolLimits limits)
    {
        this.data = data;
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
            if (!await data.ReadMoreAsync(cancellationToken).ConfigureAwait(false))
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

            do
            {
                if (!data.Buffered.IsEmpty)
                {
                    throw new AssociationImportException("Trailing data after object_list entries.");
                }
            }
            while (await data.ReadMoreAsync(cancellationToken).ConfigureAwait(false));
        }
        finally
        {
            await DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        Volatile.Write(ref disposed, 1);
        return data.DisposeAsync();
    }

    private async ValueTask<AssociationObjectEntry> ReadEntryAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            if (TryReadBufferedEntry() is { } entry)
            {
                return entry;
            }

            if (!await data.ReadMoreAsync(cancellationToken).ConfigureAwait(false))
            {
                throw new AssociationImportException("Incomplete object_list entry.");
            }
        }
    }

    private AssociationObjectEntry? TryReadBufferedEntry()
    {
        var pending = data.Buffered;
        if (pending.IsEmpty)
        {
            return null;
        }

        var decoded = codec.Decode(new ReadOnlySequence<byte>(pending.ToArray()));
        if (decoded.Status == DecodeStatus.NeedMoreData)
        {
            return null;
        }

        if (decoded.Status != DecodeStatus.Done)
        {
            throw new AssociationImportException($"Invalid object_list entry: {decoded.Status}; {decoded.Error}.");
        }

        try
        {
            var value = decoded.Value!;
            var parsed = importer.ParseEntry(value, associationVersion);
            data.Advance(checked((int)decoded.Consumed));
            return new(value, parsed.Descriptor, parsed.Name, parsed.Rights);
        }
        catch (Exception error) when (error is ArgumentException or FormatException or InvalidOperationException or OverflowException)
        {
            throw new AssociationImportException($"Invalid object_list entry: {error.Message}", error);
        }
    }

    private bool TryReadHeader()
    {
        var pending = data.Buffered;
        if (pending.IsEmpty)
        {
            return false;
        }

        if (pending[0] != (byte)DlmsType.Array)
        {
            throw new AssociationImportException("object_list must be an A-XDR array.");
        }

        if (pending.Length < 2)
        {
            return false;
        }

        byte first = pending[1];
        int lengthBytes = first < 0x80 ? 0 : first & 0x7F;
        if (first >= 0x80 && lengthBytes is 0 or > 4)
        {
            throw new AssociationImportException("Invalid object_list array length.");
        }

        if (pending.Length < 2 + lengthBytes)
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
        data.Advance(2 + lengthBytes);
        return true;
    }
}
