using System.Buffers;
using EMCL.Core.Codecs;
using EMCL.Core.Objects;
using EMCL.Core.Values;
using EMCL.Objects.Spodes;
using EMCL.Protocol.Apdu;
using EMCL.Protocol.Security;
using EMCL.Transport.Channels;

namespace EMCL.Messaging;

/// <summary>Delivers Push over an application-owned complete-APDU channel.</summary>
/// <remarks>Use a channel reserved for this sender or have the application route incoming confirmations to it.</remarks>
public sealed class SpodesPushChannel : ISpodesPushTransport
{
    private readonly IApduChannel channel;
    private readonly IApduProtection? protection;
    private readonly ApduCodec codec;
    private readonly TimeSpan timeout;
    private readonly ProtocolLimits limits;
    private readonly int maximumPduSize;
    private readonly SemaphoreSlim gate = new(1, 1);

    /// <summary>Creates a Push sender using the supplied channel and optional protection.</summary>
    /// <param name="channel">The application-owned channel to the configured destination.</param>
    /// <param name="timeout">The maximum duration of a send and application confirmation.</param>
    /// <param name="protection">The protection applied on an established or pre-established association.</param>
    /// <param name="limits">Optional bounds for encoded APDUs.</param>
    /// <param name="maximumPduSize">An optional negotiated or pre-established wire limit. Enables general block transfer for larger messages.</param>
    public SpodesPushChannel(IApduChannel channel, TimeSpan timeout, IApduProtection? protection = null,
        ProtocolLimits? limits = null, int? maximumPduSize = null)
    {
        this.channel = channel ?? throw new ArgumentNullException(nameof(channel));
        if (timeout <= TimeSpan.Zero || timeout.TotalMilliseconds > uint.MaxValue - 1)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout));
        }

        this.timeout = timeout;
        this.protection = protection;
        this.limits = ProtocolLimits.Resolve(limits);
        this.maximumPduSize = maximumPduSize ?? this.limits.MaxMessageBytes;
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(this.maximumPduSize, nameof(maximumPduSize));
        codec = new(this.limits);
    }

    /// <inheritdoc />
    public async ValueTask<bool> SendAsync(LogicalName source, DlmsValue destination,
        DataNotification notification, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(timeout);
            byte[] payload = codec.EncodeBytes(notification);
            if (protection is not null)
            {
                payload = protection.Protect(payload, true);
            }

            if (payload.Length > maximumPduSize)
            {
                return await SendBlocksAsync(payload, notification, deadline.Token).ConfigureAwait(false);
            }

            await channel.SendAsync(payload, deadline.Token).ConfigureAwait(false);
            if (!notification.IsConfirmed)
            {
                return true;
            }

            while (await channel.ReceiveAsync(deadline.Token).ConfigureAwait(false) is { } response)
            {
                if (MatchesConfirmation(response, notification))
                {
                    return true;
                }
            }

            return false;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }
        finally
        {
            gate.Release();
        }
    }

    private async ValueTask<bool> SendBlocksAsync(byte[] payload, DataNotification notification, CancellationToken ct)
    {
        int capacity = maximumPduSize - 12;
        int bound = Math.Min(ushort.MaxValue - 1, limits.MaxBlockCount);
        if (payload.Length > limits.MaxMessageBytes || capacity <= 0 || (payload.Length + (long)capacity - 1) / capacity > bound)
        {
            throw new InvalidDataException("The Push message exceeds the configured block transfer bounds.");
        }

        ushort peerBlock = 0;
        int offset = 0;
        for (ushort number = 1; offset < payload.Length; number++)
        {
            int count = Math.Min(capacity, payload.Length - offset);
            bool last = offset + count == payload.Length;
            var data = new GeneralBlockTransfer(last, false, notification.IsConfirmed ? (byte)1 : (byte)0,
                number, peerBlock, [.. payload.AsSpan(offset, count)]);
            await channel.SendAsync(codec.EncodeBytes(data), ct).ConfigureAwait(false);
            offset += count;
            if (!notification.IsConfirmed)
            {
                continue;
            }

            while (await channel.ReceiveAsync(ct).ConfigureAwait(false) is { } response)
            {
                if (Decode(response) is not GeneralBlockTransfer ack || ack.WindowSize == 0 || ack.Streaming || ack.BlockNumber == 0
                    || ack.BlockNumberAcknowledged > number || ack.BlockNumber > peerBlock + 1)
                {
                    throw new InvalidDataException("Invalid general block acknowledgement for Push.");
                }

                if (ack.BlockNumber <= peerBlock || ack.BlockNumberAcknowledged < number)
                {
                    continue;
                }

                peerBlock = ack.BlockNumber;
                if (last)
                {
                    return ack.LastBlock && !ack.Data.IsEmpty && MatchesConfirmation(ack.Data.ToArray(), notification);
                }

                if (ack.LastBlock || !ack.Data.IsEmpty)
                {
                    throw new InvalidDataException("Unexpected final general block during Push transfer.");
                }

                break;
            }

            if (peerBlock != number)
            {
                return false;
            }
        }

        return !notification.IsConfirmed;
    }

    private bool MatchesConfirmation(ReadOnlyMemory<byte> response, DataNotification notification)
    {
        var clear = protection is null ? response : protection.Unprotect(response.Span, true);
        if (Decode(clear) is not DataNotificationConfirm confirmed)
        {
            throw new InvalidDataException("Expected one complete DataNotificationConfirm.");
        }

        return confirmed.LongInvokeIdAndPriority == notification.LongInvokeIdAndPriority;
    }

    private DlmsApdu Decode(ReadOnlyMemory<byte> payload)
    {
        var decoded = codec.Decode(new ReadOnlySequence<byte>(payload));
        if (decoded.Status != DecodeStatus.Done || decoded.Consumed != payload.Length || decoded.Value is null)
        {
            throw new InvalidDataException("Expected one complete Push APDU.");
        }

        return decoded.Value;
    }
}
