using System.Collections.Immutable;
using System.Numerics;
using EMCL.Client.Internal;
using EMCL.Core.Codecs;
using EMCL.Core.Operations;
using EMCL.Core.Values;
using EMCL.Protocol.Apdu;
using EMCL.Protocol.Axdr;

namespace EMCL.Client.Sessions;

public sealed partial class DlmsClientSession
{
    private const uint InitialSetBlockNumber = 1;
    private const int ShortLengthLimit = 128;
    private const int BitsPerOctet = 8;

    private async ValueTask<LnResponse> ExecuteSetAsync(CosemOperation operation, PendingOperation pending,
        ExchangeDeadline deadline, Action onAttempt)
    {
        // Encode the value separately because the complete normal APDU may exceed the local message limit.
        byte[] data = new AxdrCodec(options.Limits).EncodeBytes(operation.Value!);
        int normalHeader;
        lock (codecGate)
        {
            normalHeader = codec.EncodeBytes(new LnRequest(pending.Invoke, operation with
            {
                Value = DlmsValue.Null
            })).Length - 1;
        }

        int limit = SendLimit();
        if ((long)normalHeader + data.Length <= limit
            && ProtectedLength(ApduTag.SetRequest, normalHeader + data.Length) <= limit)
        {
            await SendAsync(new LnRequest(pending.Invoke, operation), limit, deadline.Token, onAttempt).ConfigureAwait(false);
            return await ReceiveOperationAsync(pending, null, deadline.Token, deadline.ProcessingToken).ConfigureAwait(false);
        }

        if ((negotiated!.Conformance & DlmsConformance.BlockTransferWithSet) == DlmsConformance.None)
        {
            if (HasGeneralTransfer())
            {
                await SendApplicationAsync(new LnRequest(pending.Invoke, operation), limit, deadline.Token, onAttempt).ConfigureAwait(false);
                return await ReceiveOperationAsync(pending, null, deadline.Token, deadline.ProcessingToken).ConfigureAwait(false);
            }

            throw new NotSupportedException("The SET APDU exceeds the peer or local PDU limit and SET block transfer was not negotiated.");
        }

        return await ExecuteSetBlocksAsync(operation, pending, data, limit, deadline, onAttempt).ConfigureAwait(false);
    }

    private async ValueTask<LnResponse> ExecuteSetBlocksAsync(CosemOperation operation, PendingOperation pending,
        byte[] data, int limit, ExchangeDeadline deadline, Action onAttempt)
    {
        var first = new SetFirstBlockRequest(pending.Invoke, operation with
        {
            Value = null
        }, false, InitialSetBlockNumber, []);
        var (firstCapacity, nextCapacity) = GetSetBlockCapacities(first, limit);
        if (firstCapacity <= 0 || nextCapacity <= 0)
        {
            throw new ArgumentException("The PDU limit cannot hold the SET block headers and a data octet.", nameof(operation));
        }

        int remainingBytes = Math.Max(0, data.Length - firstCapacity);
        long blockCount = 1 + ((long)remainingBytes + nextCapacity - 1) / nextCapacity;
        if (blockCount > options.Limits.MaxBlockCount)
        {
            throw new ArgumentException("The SET transfer exceeds the configured block count limit.", nameof(operation));
        }

        int offset = 0;
        for (uint number = InitialSetBlockNumber; ; number++)
        {
            deadline.Token.ThrowIfCancellationRequested();
            int size = Math.Min(number == InitialSetBlockNumber ? firstCapacity : nextCapacity, data.Length - offset);
            bool last = offset + size == data.Length;
            var fragment = data.AsSpan(offset, size).ToArray().ToImmutableArray();
            DlmsApdu request = number == InitialSetBlockNumber
                ? first with
                {
                    LastBlock = last,
                    Data = fragment
                }
                : new SetBlockRequest(pending.Invoke, last, number, fragment);
            lock (gate)
            {
                pending.PrepareSetBlock(number, last);
            }

            await SendAsync(request, limit, deadline.Token, onAttempt).ConfigureAwait(false);
            var response = await pending.ReceiveAsync(deadline.Token, deadline.ProcessingToken).ConfigureAwait(false);
            if (response is LnResponse rejection)
            {
                return rejection;
            }

            var acknowledgement = (SetBlockResponse)response;
            if (acknowledgement.Result is { } result)
            {
                return new(CosemOperationKind.Write, pending.Invoke, new(result));
            }

            offset += size;
        }
    }

    private (int First, int Next) GetSetBlockCapacities(SetFirstBlockRequest first, int limit)
    {
        var next = new SetBlockRequest(first.InvokeIdAndPriority, false, InitialSetBlockNumber, []);
        lock (codecGate)
        {
            return (BlockPayloadCapacity(limit, codec.EncodeBytes(first).Length, ApduTag.SetRequest),
                BlockPayloadCapacity(limit, codec.EncodeBytes(next).Length, ApduTag.SetRequest));
        }
    }

    // The empty encoded block includes a one-octet length. Growing raw-data may also grow that determinant.
    private int BlockPayloadCapacity(int limit, int emptyBlockSize, ApduTag tag, bool protect = true)
    {
        int low = 0;
        int high = Math.Max(0, limit - emptyBlockSize);
        while (low < high)
        {
            int capacity = low + (high - low + 1) / 2;
            int lengthSize = capacity < ShortLengthLimit ? 1
                : 1 + (sizeof(int) * BitsPerOctet - BitOperations.LeadingZeroCount((uint)capacity) + BitsPerOctet - 1) / BitsPerOctet;
            long plainLength = (long)emptyBlockSize - 1 + lengthSize + capacity;
            if (plainLength <= limit && (!protect || ProtectedLength(tag, (int)plainLength) <= limit))
            {
                low = capacity;
            }
            else
            {
                high = capacity - 1;
            }
        }

        return low;
    }

    private int ProtectedLength(ApduTag tag, int length)
        => options.Association.Security?.GetProtectedLength((byte)tag, length, true) ?? length;
}
