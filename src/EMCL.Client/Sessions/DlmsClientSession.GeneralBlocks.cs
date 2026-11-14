using System.Buffers;
using EMCL.Client.Exceptions;
using EMCL.Core.Codecs;
using EMCL.Protocol.Acse;
using EMCL.Protocol.Apdu;

namespace EMCL.Client.Sessions;

public sealed partial class DlmsClientSession
{
    private readonly SemaphoreSlim generalSendGate = new(1, 1);
    private readonly SemaphoreSlim protectedSendGate = new(1, 1);
    private readonly object generalGate = new();
    private IncomingGeneralBlocks? generalIncoming;
    private OutgoingGeneralBlocks? generalOutgoing;

    private async ValueTask SendApplicationAsync(DlmsApdu apdu, int limit, CancellationToken ct, Action onAttempt)
    {
        byte[] clear;
        lock (codecGate)
        {
            clear = codec.EncodeBytes(apdu);
        }

        if (ProtectedLength((ApduTag)clear[0], clear.Length) <= limit)
        {
            await SendAsync(apdu, limit, ct, onAttempt).ConfigureAwait(false);
            return;
        }

        if (!HasGeneralTransfer())
        {
            throw new NotSupportedException("The APDU exceeds the PDU limit and general block transfer was not negotiated.");
        }

        await generalSendGate.WaitAsync(ct).ConfigureAwait(false);
        bool protectionLocked = false;
        OutgoingGeneralBlocks? outgoing = null;
        try
        {
            if (options.Association.Security is not null)
            {
                await protectedSendGate.WaitAsync(ct).ConfigureAwait(false);
                protectionLocked = true;
            }

            outgoing = new OutgoingGeneralBlocks();
            lock (generalGate)
            {
                if (generalIncoming is not null || generalOutgoing is not null)
                {
                    throw new DlmsProtocolException("An incoming general block transfer is already active.");
                }

                generalOutgoing = outgoing;
            }

            int capacity = BlockPayloadCapacity(limit, 7, ApduTag.GeneralBlockTransfer, protect: false);
            int length = ProtectedLength((ApduTag)clear[0], clear.Length);
            if (length > options.Limits.MaxMessageBytes || capacity == 0
                || (length + (long)capacity - 1) / capacity > Math.Min(ushort.MaxValue - 1, options.Limits.MaxBlockCount))
            {
                throw new ArgumentException("The general block transfer exceeds the configured size or block limit.", nameof(apdu));
            }

            byte[] encoded = options.Association.Security is { } security ? security.Protect(clear, true) : clear;
            if (encoded.Length != length)
            {
                throw new DlmsProtocolException("The protection protocol produced an unexpected APDU length.");
            }

            int offset = 0;
            for (ushort blockNumber = 1; offset < encoded.Length; blockNumber++)
            {
                int count = Math.Min(capacity, encoded.Length - offset);
                bool last = count == encoded.Length - offset;
                var wait = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                ushort acknowledged;
                lock (generalGate)
                {
                    outgoing.Number = blockNumber;
                    outgoing.Last = last;
                    outgoing.Acknowledged = wait;
                    acknowledged = generalIncoming?.LastAccepted ?? 0;
                }

                await SendAsync(new GeneralBlockTransfer(last, false, 1, blockNumber, acknowledged,
                    [.. encoded.AsSpan(offset, count)]), limit, ct, onAttempt).ConfigureAwait(false);
                if (!last)
                {
                    await wait.Task.WaitAsync(ct).ConfigureAwait(false);
                }

                offset += count;
            }
        }
        catch
        {
            lock (generalGate)
            {
                if (ReferenceEquals(generalOutgoing, outgoing))
                {
                    generalOutgoing = null;
                }
            }

            throw;
        }
        finally
        {
            if (protectionLocked)
            {
                protectedSendGate.Release();
            }

            generalSendGate.Release();
        }
    }

    private async ValueTask<(DlmsApdu? Apdu, GeneralConfirmation? Confirmation)> ReceiveGeneralBlockAsync(
        GeneralBlockTransfer block, bool authenticating, CancellationToken ct)
    {
        if (!HasGeneralTransfer() || authenticating || block.BlockNumber == 0
            || block.Streaming && (block.WindowSize == 0 || block.LastBlock))
        {
            throw new DlmsProtocolException("The general block transfer header is invalid in this association.");
        }

        byte[]? completed = null;
        GeneralBlockTransfer? acknowledge = null;
        GeneralConfirmation? confirmation = null;
        lock (generalGate)
        {
            var outgoing = generalOutgoing;
            var incoming = generalIncoming ??= new IncomingGeneralBlocks(block.WindowSize != 0);
            if (block.WindowSize == 0 && incoming.Confirmed || block.WindowSize != 0 && !incoming.Confirmed
                || block.BlockNumberAcknowledged > (outgoing?.Number ?? incoming.LocalBlock))
            {
                throw new DlmsProtocolException("The general block transfer acknowledgement or window is invalid.");
            }

            if (block.BlockNumber != incoming.LastAccepted + 1)
            {
                if (!incoming.Confirmed)
                {
                    throw new DlmsProtocolException("A general block is out of sequence.");
                }

                // Request recovery at the last contiguous block. Duplicate fragments are never appended.
                acknowledge = Ack(incoming, outgoing);
            }
            else
            {
                incoming.LastAccepted = block.BlockNumber;
                if (outgoing is not null && block.BlockNumberAcknowledged == outgoing.Number)
                {
                    outgoing.Acknowledged?.TrySetResult();
                }

                if (block.BlockNumber > options.Limits.MaxBlockCount
                    || (long)incoming.Bytes.WrittenCount + block.Data.Length > options.Limits.MaxMessageBytes)
                {
                    throw new DlmsProtocolException("The general block transfer exceeds a configured bound.");
                }

                if (!block.Data.IsDefaultOrEmpty)
                {
                    incoming.Bytes.Write(block.Data.AsSpan());
                }

                if (block.LastBlock)
                {
                    if (outgoing is { Last: false } || outgoing is not null && block.BlockNumberAcknowledged != outgoing.Number
                        || incoming.Bytes.WrittenCount == 0)
                    {
                        throw new DlmsProtocolException("The final general block does not complete the exchange.");
                    }

                    completed = incoming.Bytes.WrittenSpan.ToArray();
                    if (incoming.Confirmed && outgoing is null)
                    {
                        confirmation = new(checked((ushort)(incoming.LocalBlock + 1)), incoming.LastAccepted);
                    }

                    generalIncoming = null;
                    generalOutgoing = null;
                }
                else if (incoming.Confirmed && !block.Streaming && (outgoing is null || outgoing.Last))
                {
                    acknowledge = Ack(incoming, outgoing);
                }
            }
        }

        if (acknowledge is not null)
        {
            await SendAsync(acknowledge, SendLimit(), ct).ConfigureAwait(false);
        }

        if (completed is null)
        {
            return (null, null);
        }

        var apdu = DecodeApplication(completed, true);
        if (apdu is GeneralBlockTransfer or AcseApdu)
        {
            throw new DlmsProtocolException("A general block transfer contains an invalid nested APDU.");
        }

        if (apdu is DataNotification notification && notification.IsConfirmed != (confirmation is not null))
        {
            throw new DlmsProtocolException("The notification service class disagrees with its general block transfer window.");
        }

        return (apdu, apdu is DataNotification { IsConfirmed: true } ? confirmation : null);
    }

    private static GeneralBlockTransfer Ack(IncomingGeneralBlocks incoming, OutgoingGeneralBlocks? outgoing)
    {
        if (incoming.LocalBlock != 0 && incoming.LocalAcknowledged == incoming.LastAccepted)
        {
            return new(outgoing is { Last: true }, false, 1, incoming.LocalBlock, incoming.LastAccepted, []);
        }

        ushort number = checked((ushort)(Math.Max(incoming.LocalBlock, outgoing?.Number ?? 0) + 1));
        incoming.LocalBlock = number;
        incoming.LocalAcknowledged = incoming.LastAccepted;
        return new(outgoing is { Last: true }, false, 1, number, incoming.LastAccepted, []);
    }

    private bool HasGeneralTransfer()
        => (NegotiatedParameters?.Conformance & DlmsConformance.GeneralBlockTransfer) == DlmsConformance.GeneralBlockTransfer;

    private sealed class IncomingGeneralBlocks(bool confirmed)
    {
        internal bool Confirmed { get; } = confirmed;
        internal ArrayBufferWriter<byte> Bytes { get; } = new();
        internal ushort LastAccepted
        {
            get; set;
        }
        internal ushort LocalBlock
        {
            get; set;
        }
        internal ushort LocalAcknowledged
        {
            get; set;
        }
    }

    private sealed class OutgoingGeneralBlocks
    {
        internal ushort Number
        {
            get; set;
        }
        internal bool Last
        {
            get; set;
        }
        internal TaskCompletionSource? Acknowledged
        {
            get; set;
        }
    }

    private sealed record GeneralConfirmation(ushort Number, ushort Acknowledged);
}
