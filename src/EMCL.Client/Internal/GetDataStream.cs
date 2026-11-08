using System.Collections.Immutable;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Threading.Channels;

namespace EMCL.Client.Internal;

internal sealed class GetDataStream(CancellationToken cancellationToken, bool assembleBlocks) : IAsyncDisposable
{
    private readonly CancellationTokenSource source = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
    private readonly Channel<byte[]> chunks = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(1)
    {
        SingleReader = true,
        SingleWriter = true,
        AllowSynchronousContinuations = false
    });
    private readonly List<byte> pending = [];
    private readonly Lock disposeGate = new();
    private int offset;
    private Task? disposal;

    internal bool AssembleBlocks { get; } = assembleBlocks;
    internal bool ReceivedBlocks
    {
        get; private set;
    }
    internal CancellationToken Token => source.Token;
    internal Task Producer { private get; set; } = Task.CompletedTask;
    internal ReadOnlySpan<byte> Buffered => CollectionsMarshal.AsSpan(pending)[offset..];

    internal ValueTask WriteBlockAsync(ImmutableArray<byte> data, CancellationToken cancellationToken)
    {
        ReceivedBlocks = true;
        return chunks.Writer.WriteAsync(data.ToArray(), cancellationToken);
    }

    internal ValueTask WriteAsync(byte[] data) => chunks.Writer.WriteAsync(data, Token);

    internal void Complete(Exception? error = null) => chunks.Writer.TryComplete(error);

    internal void Advance(int count) => offset += count;

    internal async ValueTask<bool> ReadMoreAsync(CancellationToken cancellationToken)
    {
        try
        {
            var chunk = await chunks.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);
            if (offset != 0)
            {
                pending.RemoveRange(0, offset);
                offset = 0;
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

    public ValueTask DisposeAsync()
    {
        lock (disposeGate)
        {
            return new(disposal ??= DisposeCoreAsync());
        }
    }

    private async Task DisposeCoreAsync()
    {
        try
        {
            await source.CancelAsync().ConfigureAwait(false);
        }
        finally
        {
            try
            {
                await Producer.ConfigureAwait(false);
            }
            finally
            {
                source.Dispose();
            }
        }
    }
}
