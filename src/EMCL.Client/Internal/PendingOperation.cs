using System.Threading.Channels;
using EMCL.Client.Exceptions;
using EMCL.Core.Operations;
using EMCL.Protocol.Apdu;

namespace EMCL.Client.Internal;

internal sealed class PendingOperation(byte invoke, CosemOperationKind kind)
{
    private readonly Channel<DlmsApdu> responses = Channel.CreateBounded<DlmsApdu>(new BoundedChannelOptions(1)
    {
        SingleReader = true,
        SingleWriter = true,
        AllowSynchronousContinuations = false
    });
    private bool waiting = true;
    private bool blocksStarted;
    private const uint FirstBlockNumber = 1;
    private uint expectedBlockNumber = FirstBlockNumber;

    internal byte Invoke { get; } = invoke;
    internal byte Id => (byte)(Invoke & InvokeIdEncoding.IdMask);

    // The session gate protects all routing state, including preparation for the next GET block.
    internal void Deliver(DlmsApdu response)
    {
        if (!waiting)
        {
            throw new DlmsProtocolException("A duplicate or unsolicited response was received for an active invoke ID.");
        }

        bool final;
        switch (response)
        {
            case LnResponse normal when normal.InvokeIdAndPriority == Invoke && normal.Kind == kind && !blocksStarted:
                final = true;
                break;
            case GetBlockResponse block when block.InvokeIdAndPriority == Invoke && kind == CosemOperationKind.Read:
                if (block.BlockNumber != expectedBlockNumber)
                {
                    throw new DlmsProtocolException("The GET block number is out of sequence.");
                }

                if (expectedBlockNumber != uint.MaxValue)
                {
                    expectedBlockNumber++;
                }

                blocksStarted = true;
                final = block.LastBlock || block.Error.HasValue;
                break;
            default:
                throw new DlmsProtocolException("The response service or invoke-id-and-priority does not match the request.");
        }

        waiting = false;
        if (!responses.Writer.TryWrite(response))
        {
            throw new DlmsProtocolException("The pending response buffer is full.");
        }

        if (final)
        {
            responses.Writer.TryComplete();
        }
    }

    internal void PrepareNextBlock()
    {
        if (waiting || !blocksStarted)
        {
            throw new InvalidOperationException("The GET exchange is not ready for a continuation request.");
        }

        waiting = true;
    }

    internal async ValueTask<DlmsApdu> ReceiveAsync(CancellationToken cancellationToken,
        CancellationToken processingToken)
    {
        processingToken.ThrowIfCancellationRequested();
        // EOF may cancel the session after a validated response was already buffered.
        if (responses.Reader.TryRead(out var buffered))
        {
            return buffered;
        }

        try
        {
            return await responses.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (ChannelClosedException error) when (error.InnerException != null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error.InnerException).Throw();
            throw;
        }
    }

    internal void Fail(Exception error) => responses.Writer.TryComplete(error);
}
