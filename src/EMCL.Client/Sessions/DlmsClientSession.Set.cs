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
        // Encode the value independently: the complete normal APDU may exceed even the local message limit.
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
        if ((long)normalHeader + data.Length <= limit)
        {
            await SendAsync(new LnRequest(pending.Invoke, operation), limit, deadline.Token, onAttempt).ConfigureAwait(false);
            return await ReceiveOperationAsync(pending, null, deadline.Token, deadline.ProcessingToken).ConfigureAwait(false);
        }

        if ((negotiated!.Conformance & DlmsConformance.BlockTransferWithSet) == DlmsConformance.None)
        {
            throw new NotSupportedException("The SET APDU exceeds the peer or local PDU limit and SET block transfer was not negotiated.");
        }

        var first = new SetFirstBlockRequest(pending.Invoke, operation with
        {
            Value = null
        }, false, InitialSetBlockNumber, []);
        int firstCapacity;
        int nextCapacity;
        lock (codecGate)
        {
            firstCapacity = SetPayloadCapacity(limit, codec.EncodeBytes(first).Length);
            nextCapacity = SetPayloadCapacity(limit, codec.EncodeBytes(new SetBlockRequest(pending.Invoke, false, InitialSetBlockNumber, [])).Length);
        }

        if (firstCapacity <= 0 || nextCapacity <= 0)
        {
            throw new ArgumentException("The PDU limit cannot hold the SET block headers and a data octet.", nameof(operation));
        }

        long count = 1 + ((long)Math.Max(0, data.Length - firstCapacity) + nextCapacity - 1) / nextCapacity;
        if (count > options.Limits.MaxBlockCount || count > uint.MaxValue)
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

    // The empty encoded block includes a one-octet length. Growing raw-data may also grow that determinant.
    private static int SetPayloadCapacity(int limit, int emptyBlockSize)
    {
        int capacity = Math.Max(0, limit - emptyBlockSize);
        while (capacity > 0)
        {
            int lengthSize = capacity < ShortLengthLimit ? 1
                : 1 + (sizeof(int) * BitsPerOctet - BitOperations.LeadingZeroCount((uint)capacity) + BitsPerOctet - 1) / BitsPerOctet;
            if ((long)emptyBlockSize - 1 + lengthSize + capacity <= limit)
            {
                break;
            }

            capacity--;
        }

        return capacity;
    }
}
