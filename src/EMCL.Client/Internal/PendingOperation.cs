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
    private bool setBlocksStarted;
    private bool lastSetBlock;

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
                if (setBlocksStarted && normal.Result.Error == CosemError.Success)
                {
                    throw new DlmsProtocolException("A SET block transfer requires a final block result.");
                }

                final = true;
                break;
            case SetBlockResponse block when block.InvokeIdAndPriority == Invoke && kind == CosemOperationKind.Write && setBlocksStarted:
                if (block.BlockNumber != expectedBlockNumber || lastSetBlock && !block.Result.HasValue
                    || !lastSetBlock && block.Result == CosemError.Success)
                {
                    throw new DlmsProtocolException("The SET block acknowledgement number or final result is invalid.");
                }

                final = block.Result.HasValue;
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

    // Called under the session gate before sending each block, so immediate replies can be correlated.
    internal void PrepareSetBlock(uint number, bool last)
    {
        if (kind != CosemOperationKind.Write || setBlocksStarted && waiting)
        {
            throw new InvalidOperationException("The SET exchange is not ready for a block request.");
        }

        setBlocksStarted = true;
        expectedBlockNumber = number;
        lastSetBlock = last;
        waiting = true;
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
