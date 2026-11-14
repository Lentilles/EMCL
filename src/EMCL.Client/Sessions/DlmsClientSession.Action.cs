using System.Buffers;
using System.Collections.Immutable;
using EMCL.Client.Exceptions;
using EMCL.Client.Internal;
using EMCL.Core.Codecs;
using EMCL.Core.Operations;
using EMCL.Protocol.Apdu;
using EMCL.Protocol.Axdr;

namespace EMCL.Client.Sessions;

public sealed partial class DlmsClientSession
{
    private const uint FirstActionBlock = 1;

    private async ValueTask<LnResponse> ExecuteActionAsync(CosemOperation operation, PendingOperation pending,
        ExchangeDeadline deadline, Action onAttempt)
    {
        var request = new LnRequest(pending.Invoke, operation);
        int header;
        lock (codecGate)
        {
            header = codec.EncodeBytes(new LnRequest(pending.Invoke, operation with
            {
                Value = null
            })).Length;
        }

        byte[] data = operation.Value is { } value ? new AxdrCodec(options.Limits).EncodeBytes(value) : [];
        int limit = SendLimit();
        if ((long)header + data.Length <= limit && ProtectedLength(ApduTag.ActionRequest, header + data.Length) <= limit)
        {
            await SendAsync(request, limit, deadline.Token, onAttempt).ConfigureAwait(false);
            return await ReceiveOperationAsync(pending, null, deadline.Token, deadline.ProcessingToken).ConfigureAwait(false);
        }

        if (data.Length == 0 || (negotiated!.Conformance & DlmsConformance.BlockTransferWithAction) == DlmsConformance.None)
        {
            if (HasGeneralTransfer())
            {
                await SendApplicationAsync(request, limit, deadline.Token, onAttempt).ConfigureAwait(false);
                return await ReceiveOperationAsync(pending, null, deadline.Token, deadline.ProcessingToken).ConfigureAwait(false);
            }

            throw new NotSupportedException("The ACTION exceeds the PDU limit and ACTION block transfer is unavailable.");
        }

        var first = new ActionBlock(pending.Invoke, false, false, FirstActionBlock, [], operation with
        {
            Value = null
        });
        int firstCapacity;
        int nextCapacity;
        lock (codecGate)
        {
            firstCapacity = BlockPayloadCapacity(limit, codec.EncodeBytes(first).Length, ApduTag.ActionRequest);
            nextCapacity = BlockPayloadCapacity(limit, codec.EncodeBytes(first with
            {
                Method = null
            }).Length, ApduTag.ActionRequest);
        }

        if (firstCapacity == 0 || nextCapacity == 0 ||
            1 + (Math.Max(0, data.Length - firstCapacity) + (long)nextCapacity - 1) / nextCapacity > options.Limits.MaxBlockCount)
        {
            throw new ArgumentException("The ACTION exceeds the configured block size or count limit.", nameof(operation));
        }

        int offset = 0;
        for (uint number = FirstActionBlock; ; number++)
        {
            int size = Math.Min(data.Length - offset, number == FirstActionBlock ? firstCapacity : nextCapacity);
            bool last = offset + size == data.Length;
            var block = first with
            {
                BlockNumber = number,
                LastBlock = last,
                Data = data.AsSpan(offset, size).ToArray().ToImmutableArray(),
                Method = number == FirstActionBlock ? first.Method : null
            };
            lock (gate)
            {
                pending.PrepareActionRequestBlock(number, last);
            }

            await SendAsync(block, limit, deadline.Token, onAttempt).ConfigureAwait(false);
            if (last)
            {
                return await ReceiveOperationAsync(pending, null, deadline.Token, deadline.ProcessingToken).ConfigureAwait(false);
            }

            if (await pending.ReceiveAsync(deadline.Token, deadline.ProcessingToken).ConfigureAwait(false) is LnResponse rejected)
            {
                return rejected;
            }

            offset += size;
        }
    }

    private async ValueTask<LnResponse> ReceiveActionBlocksAsync(PendingOperation pending, ActionBlock block,
        CancellationToken cancellationToken, CancellationToken processingToken)
    {
        if ((negotiated!.Conformance & DlmsConformance.BlockTransferWithAction) == DlmsConformance.None)
        {
            throw new DlmsProtocolException("The peer used ACTION block transfer without negotiation.");
        }

        var assembled = new ArrayBufferWriter<byte>();
        byte[] header = [(byte)ApduTag.ActionResponse, ApduEncoding.NormalChoice, pending.Invoke];
        assembled.Write(header);
        while (true)
        {
            if (block.BlockNumber > options.Limits.MaxBlockCount || block.Data.IsDefaultOrEmpty
                || (long)assembled.WrittenCount + block.Data.Length > options.Limits.MaxMessageBytes)
            {
                throw new DlmsProtocolException("The ACTION response exceeds the block or byte limit.");
            }

            assembled.Write(block.Data.AsSpan());
            if (block.LastBlock)
            {
                DecodeResult<DlmsApdu> decoded;
                lock (codecGate)
                {
                    decoded = codec.Decode(new ReadOnlySequence<byte>(assembled.WrittenMemory));
                }

                if (decoded.Status != DecodeStatus.Done || decoded.Consumed != assembled.WrittenCount
                    || decoded.Value is not LnResponse { Kind: CosemOperationKind.Invoke } result)
                {
                    throw new DlmsProtocolException("The ACTION blocks do not contain one complete response result.");
                }

                return result;
            }

            lock (gate)
            {
                pending.PrepareActionResponseBlock();
            }

            await SendAsync(new ActionNextBlock(pending.Invoke, false, block.BlockNumber), SendLimit(),
                cancellationToken).ConfigureAwait(false);
            block = (ActionBlock)await pending.ReceiveAsync(cancellationToken, processingToken).ConfigureAwait(false);
        }
    }
}
