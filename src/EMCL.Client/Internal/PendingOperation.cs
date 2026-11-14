using System.Threading.Channels;
using EMCL.Client.Exceptions;
using EMCL.Core.Operations;
using EMCL.Protocol.Apdu;

namespace EMCL.Client.Internal;

internal sealed class PendingOperation(byte invoke, CosemOperationKind kind, bool ignoreInvokeIdValidation)
{
    private readonly Channel<DlmsApdu> responses = Channel.CreateBounded<DlmsApdu>(new BoundedChannelOptions(1)
    {
        SingleReader = true,
        SingleWriter = true,
        AllowSynchronousContinuations = false
    });
    private bool waiting = true;
    private bool getBlocksStarted;
    private const uint FirstBlockNumber = 1;
    private uint expectedBlockNumber = FirstBlockNumber;
    private bool setBlocksStarted;
    private bool lastSetBlock;
    private bool actionRequestBlocks;
    private bool actionResponseBlocks;
    private bool lastActionRequestBlock;
    private uint expectedActionBlock = FirstBlockNumber;

    internal byte Invoke { get; } = invoke;
    internal byte Id => (byte)(Invoke & InvokeIdEncoding.IdMask);

    // The session gate protects routing state and preparation for GET and SET blocks.
    internal void Deliver(DlmsApdu response, byte receivedInvoke, string? stage)
    {
        if (!ignoreInvokeIdValidation && receivedInvoke != Invoke)
        {
            stage ??= kind switch
            {
                CosemOperationKind.Read => "GET",
                CosemOperationKind.Write => "SET",
                _ => "ACTION"
            };
            throw new DlmsProtocolException($"stage={stage}; Invoke-Id-And-Priority mismatch: expected=0x{Invoke:X2}, received=0x{receivedInvoke:X2}.");
        }

        if (!waiting)
        {
            throw new DlmsProtocolException("A duplicate or unsolicited response was received for an active invoke ID.");
        }

        bool completesExchange = response switch
        {
            LnResponse normal when normal.Kind == kind && !getBlocksStarted
                => AcceptNormalResponse(normal),
            SetBlockResponse block when kind == CosemOperationKind.Write && setBlocksStarted
                => AcceptSetBlock(block),
            GetBlockResponse block when kind == CosemOperationKind.Read
                => AcceptGetBlock(block),
            ActionNextBlock { Response: true } next when kind == CosemOperationKind.Invoke
                && actionRequestBlocks && !lastActionRequestBlock && next.BlockNumber == expectedActionBlock => false,
            ActionBlock { Response: true } block when kind == CosemOperationKind.Invoke
                && (!actionRequestBlocks || lastActionRequestBlock) => AcceptActionBlock(block),
            _ => throw new DlmsProtocolException("The response service does not match the request.")
        };

        waiting = false;
        if (!responses.Writer.TryWrite(response))
        {
            throw new DlmsProtocolException("The pending response buffer is full.");
        }

        if (completesExchange)
        {
            responses.Writer.TryComplete();
        }
    }

    private bool AcceptNormalResponse(LnResponse response)
    {
        if (actionResponseBlocks || actionRequestBlocks && !lastActionRequestBlock && response.Result.Error == CosemResultCode.Success)
        {
            throw new DlmsProtocolException("The ACTION result arrived before the required blocks completed.");
        }

        if (setBlocksStarted && response.Result.Error == CosemResultCode.Success)
        {
            throw new DlmsProtocolException("A SET block transfer requires a final block result.");
        }

        return true;
    }

    private bool AcceptActionBlock(ActionBlock block)
    {
        if (block.BlockNumber != expectedActionBlock)
        {
            throw new DlmsProtocolException("The ACTION response block number is out of sequence.");
        }

        expectedActionBlock = checked(expectedActionBlock + 1);
        actionResponseBlocks = true;
        return block.LastBlock;
    }

    internal void PrepareActionRequestBlock(uint number, bool last)
    {
        actionRequestBlocks = true;
        lastActionRequestBlock = last;
        expectedActionBlock = last ? FirstBlockNumber : number;
        waiting = true;
    }

    internal void PrepareActionResponseBlock() => waiting = true;

    private bool AcceptSetBlock(SetBlockResponse block)
    {
        if (block.BlockNumber != expectedBlockNumber || lastSetBlock && !block.Result.HasValue
            || !lastSetBlock && block.Result == CosemResultCode.Success)
        {
            throw new DlmsProtocolException("The SET block acknowledgement number or final result is invalid.");
        }

        return block.Result.HasValue;
    }

    private bool AcceptGetBlock(GetBlockResponse block)
    {
        if (block.BlockNumber != expectedBlockNumber)
        {
            throw new DlmsProtocolException("The GET block number is out of sequence.");
        }

        if (expectedBlockNumber != uint.MaxValue)
        {
            expectedBlockNumber++;
        }

        getBlocksStarted = true;
        return block.LastBlock || block.Error.HasValue;
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
        if (waiting || !getBlocksStarted)
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
