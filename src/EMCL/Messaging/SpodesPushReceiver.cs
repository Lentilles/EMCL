using System.Buffers;
using EMCL.Core.Codecs;
using EMCL.Protocol.Apdu;
using EMCL.Protocol.Security;
using EMCL.Transport.Channels;

namespace EMCL.Messaging;

/// <summary>Receives Push on a channel with a pre-established application association.</summary>
/// <remarks>The application owns the channel. Run a single receive loop and complete processing before reading the next notification.</remarks>
public sealed class SpodesPushReceiver
{
    private readonly IApduChannel channel;
    private readonly IApduProtection? protection;
    private readonly ProtocolLimits limits;
    private readonly ApduCodec codec;

    /// <summary>Creates a receiver for a channel dedicated to Push traffic.</summary>
    /// <param name="channel">The complete-APDU channel.</param>
    /// <param name="protection">The optional protection context for the sender.</param>
    /// <param name="limits">Optional resource bounds.</param>
    public SpodesPushReceiver(IApduChannel channel, IApduProtection? protection = null, ProtocolLimits? limits = null)
    {
        this.channel = channel ?? throw new ArgumentNullException(nameof(channel));
        this.protection = protection;
        this.limits = ProtocolLimits.Resolve(limits);
        codec = new(this.limits);
    }

    /// <summary>Receives one notification, dispatches it to the application, and confirms successful processing if requested.</summary>
    /// <param name="process">Returns true to accept the notification.</param>
    /// <param name="cancellationToken">A token that bounds receiving, processing, and confirming the message.</param>
    /// <returns>False at clean end of input, otherwise true.</returns>
    public async ValueTask<bool> ReceiveAsync(Func<DataNotification, CancellationToken, ValueTask<bool>> process,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(process);
        if (await channel.ReceiveAsync(cancellationToken).ConfigureAwait(false) is not { } payload)
        {
            return false;
        }

        GeneralBlockTransfer? final = null;
        ushort localBlock = 0;
        if (!payload.IsEmpty && payload.Span[0] == (byte)ApduTag.GeneralBlockTransfer)
        {
            var fragments = new ArrayBufferWriter<byte>();
            ushort remoteBlock = 0;
            bool? confirmed = null;
            while (true)
            {
                if (Decode(payload) is not GeneralBlockTransfer block || block.BlockNumber == 0
                    || block.BlockNumber > Math.Min(limits.MaxBlockCount, ushort.MaxValue - 1)
                    || block.Streaming && (block.WindowSize == 0 || block.LastBlock)
                    || block.BlockNumberAcknowledged > localBlock)
                {
                    throw new InvalidDataException("Invalid Push general block header.");
                }

                confirmed ??= block.WindowSize != 0;
                if (confirmed != (block.WindowSize != 0))
                {
                    throw new InvalidDataException("The Push general block confirmation mode changed.");
                }

                if (block.BlockNumber == remoteBlock + 1)
                {
                    if ((long)fragments.WrittenCount + block.Data.Length > limits.MaxMessageBytes)
                    {
                        throw new InvalidDataException("The Push general block payload exceeds the configured limit.");
                    }

                    fragments.Write(block.Data.AsSpan());
                    remoteBlock = block.BlockNumber;
                    if (block.LastBlock)
                    {
                        final = block;
                        payload = fragments.WrittenMemory;
                        break;
                    }
                }
                else if (confirmed == false)
                {
                    throw new InvalidDataException("An unconfirmed Push block is missing or out of sequence.");
                }

                if (confirmed == true && (!block.Streaming || block.BlockNumber != remoteBlock))
                {
                    bool next = block.BlockNumber == remoteBlock && block.BlockNumberAcknowledged == localBlock;
                    if (next || localBlock == 0)
                    {
                        localBlock = checked((ushort)(localBlock + 1));
                    }

                    await SendAsync(new GeneralBlockTransfer(false, false, 1, localBlock, remoteBlock, []), cancellationToken)
                        .ConfigureAwait(false);
                }

                payload = await channel.ReceiveAsync(cancellationToken).ConfigureAwait(false)
                    ?? throw new EndOfStreamException("The Push block transfer ended before its final block.");
            }
        }

        var clear = protection is null ? payload : protection.Unprotect(payload.Span, true);
        if (Decode(clear) is not DataNotification notification
            || final is not null && notification.IsConfirmed != (final.WindowSize != 0))
        {
            throw new InvalidDataException("Expected a DataNotification matching the transfer's confirmation mode.");
        }

        if (await process(notification, cancellationToken).ConfigureAwait(false) && notification.IsConfirmed)
        {
            var confirmation = new DataNotificationConfirm(notification.LongInvokeIdAndPriority, []);
            byte[] encoded = codec.EncodeBytes(confirmation);
            if (protection is not null)
            {
                encoded = protection.Protect(encoded, true);
            }

            if (final is not null)
            {
                encoded = codec.EncodeBytes(new GeneralBlockTransfer(true, false, 1,
                    checked((ushort)(localBlock + 1)), final.BlockNumber, [.. encoded]));
            }

            await channel.SendAsync(encoded, cancellationToken).ConfigureAwait(false);
        }

        return true;
    }

    private ValueTask SendAsync(DlmsApdu apdu, CancellationToken ct) => channel.SendAsync(codec.EncodeBytes(apdu), ct);

    private DlmsApdu Decode(ReadOnlyMemory<byte> payload)
    {
        var result = codec.Decode(new ReadOnlySequence<byte>(payload));
        if (result.Status != DecodeStatus.Done || result.Consumed != payload.Length || result.Value is null)
        {
            throw new InvalidDataException("Expected one complete Push APDU.");
        }

        return result.Value;
    }
}
