using System.Runtime.CompilerServices;
using EMCL.Client.Exceptions;
using EMCL.Client.Internal;
using EMCL.Core.Codecs;
using EMCL.Core.Values;
using EMCL.Protocol.Axdr;

namespace EMCL.Client.Sessions;

/// <summary>Yields Profile Generic buffer rows as they are decoded from one GET response.</summary>
/// <remarks>Enumerate once and dispose the stream when finished. Early disposal cancels an incomplete GET and may fault the session.</remarks>
public sealed class ProfileBufferStream : IAsyncEnumerable<DlmsValue>, IAsyncDisposable
{
    private readonly GetDataStream data;
    private readonly AxdrArrayReader reader;
    private readonly Action<DlmsProtocolException> fault;
    private int enumerationStarted;
    private int disposed;

    internal ProfileBufferStream(GetDataStream data, ProtocolLimits limits, Action<DlmsProtocolException> fault)
    {
        this.data = data;
        this.fault = fault;
        reader = new(limits);
    }

    /// <summary>Gets the number of rows advertised by an array response, or null for a compact-array response.</summary>
    public int? TotalCount => reader.TotalRows;

    /// <summary>Gets the number of rows yielded so far.</summary>
    public int CurrentCount => reader.RowsRead;

    /// <summary>Gets the declared content length of a compact-array response, or null for an array response.</summary>
    public int? TotalBytes => reader.TotalContentBytes;

    /// <summary>Gets the encoded byte count of rows yielded so far, excluding the array header.</summary>
    public int CurrentBytes => reader.ContentBytesRead;

    internal async ValueTask InitializeAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = reader.ReadHeader(data.Buffered);
            if (result.Status == DecodeStatus.Done)
            {
                data.Advance(checked((int)result.Consumed));
                return;
            }

            await ReadMoreAsync(result.Status, result.Error, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public IAsyncEnumerator<DlmsValue> GetAsyncEnumerator(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        if (Interlocked.Exchange(ref enumerationStarted, 1) != 0)
        {
            throw new InvalidOperationException("The profile buffer stream can be enumerated only once.");
        }

        return EnumerateAsync(cancellationToken).GetAsyncEnumerator(cancellationToken);
    }

    private async IAsyncEnumerable<DlmsValue> EnumerateAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        try
        {
            while (!reader.IsComplete)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var result = reader.ReadRow(data.Buffered);
                if (result.Status == DecodeStatus.Done)
                {
                    data.Advance(checked((int)result.Consumed));
                    yield return result.Value!;
                    continue;
                }

                await ReadMoreAsync(result.Status, result.Error, cancellationToken).ConfigureAwait(false);
            }

            do
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!data.Buffered.IsEmpty)
                {
                    throw InvalidBuffer("Trailing data after profile rows.");
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

    private async ValueTask ReadMoreAsync(DecodeStatus status, string? error, CancellationToken cancellationToken)
    {
        if (status != DecodeStatus.NeedMoreData)
        {
            throw InvalidBuffer($"{status}: {error}");
        }

        if (!await data.ReadMoreAsync(cancellationToken).ConfigureAwait(false))
        {
            throw InvalidBuffer("The response ended before the declared profile data was complete.");
        }
    }

    private DlmsProtocolException InvalidBuffer(string detail)
    {
        var error = new DlmsProtocolException($"Invalid Profile Generic buffer. {detail}");
        fault(error);
        return error;
    }
}
