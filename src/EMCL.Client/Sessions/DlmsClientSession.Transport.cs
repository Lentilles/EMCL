using System.Buffers;
using EMCL.Client.Exceptions;
using EMCL.Core.Codecs;
using EMCL.Protocol.Acse;
using EMCL.Protocol.Apdu;

namespace EMCL.Client.Sessions;

public sealed partial class DlmsClientSession
{
    private async ValueTask SendAsync(DlmsApdu apdu, int maximumBytes, CancellationToken cancellationToken,
        Action? onAttempt = null)
    {
        byte[] bytes;
        lock (codecGate)
        {
            bytes = codec.EncodeBytes(apdu);
        }

        bool protect = apdu is not (AcseApdu or GeneralBlockTransfer) && options.Association.Security is not null;
        if (protect)
        {
            await protectedSendGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        try
        {
            await SendWireAsync(apdu, bytes, maximumBytes, cancellationToken, onAttempt).ConfigureAwait(false);
        }
        finally
        {
            if (protect)
            {
                protectedSendGate.Release();
            }
        }
    }

    private async ValueTask SendWireAsync(DlmsApdu apdu, byte[] bytes, int maximumBytes, CancellationToken cancellationToken,
        Action? onAttempt)
    {
        await sendLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            lock (gate)
            {
                ObjectDisposedException.ThrowIf(state == ClientSessionState.Disposed, this);
                if (state == ClientSessionState.Faulted)
                {
                    throw new IOException("The association has faulted.", failure);
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (apdu is not (AcseApdu or GeneralBlockTransfer) && options.Association.Security is { } security)
            {
                bool established = State != ClientSessionState.Associating;
                CheckSendSize(security.GetProtectedLength(bytes[0], bytes.Length, established), maximumBytes);
                bytes = security.Protect(bytes, established);
            }

            CheckSendSize(bytes.Length, maximumBytes);
            onAttempt?.Invoke();
            await channel.SendAsync(bytes, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            sendLock.Release();
        }
    }

    private static void CheckSendSize(int length, int limit)
    {
        if (length > limit)
        {
            throw new ArgumentException("The encoded APDU exceeds the peer or local PDU limit.", "apdu");
        }
    }

    private async Task ReceiveLoopAsync()
    {
        try
        {
            while (!lifetimeToken.IsCancellationRequested)
            {
                var payload = await channel.ReceiveAsync(lifetimeToken).ConfigureAwait(false);
                if (!payload.HasValue)
                {
                    throw new EndOfStreamException("The peer closed the APDU channel.");
                }

                if (payload.Value.Length > EffectiveLimit(options.Association.MaxReceivePduSize))
                {
                    throw new DlmsProtocolException("The received APDU exceeds the advertised client or local PDU limit.");
                }

                bool authenticating = options.Association.Security is not null && State == ClientSessionState.Associating;
                var apdu = DecodeApplication(payload.Value, !authenticating);
                GeneralConfirmation? confirmation = null;
                if (apdu is GeneralBlockTransfer block)
                {
                    (apdu, confirmation) = await ReceiveGeneralBlockAsync(block, authenticating, lifetimeToken).ConfigureAwait(false);
                    if (apdu is null)
                    {
                        continue;
                    }
                }

                if (Route(apdu, confirmation))
                {
                    return;
                }

                if (apdu is AcseApdu { Kind: AcseKind.Aare })
                {
                    await associationReady.Task.WaitAsync(lifetimeToken).ConfigureAwait(false);
                }
                else if (authenticating && apdu is LnResponse { Kind: EMCL.Core.Operations.CosemOperationKind.Invoke })
                {
                    await associationAuthenticated.Task.WaitAsync(lifetimeToken).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException) when (lifetimeToken.IsCancellationRequested)
        {
            // The session initiator has already completed pending operations with the cancellation cause.
        }
        catch (Exception error)
        {
            Fault(error);
        }
    }

    private DlmsApdu DecodeApplication(ReadOnlyMemory<byte> payload, bool established)
    {
        ReadOnlyMemory<byte> clear = payload;
        if (clear.Length > 0 && !Enum.IsDefined((AcseKind)clear.Span[0])
            && clear.Span[0] != (byte)ApduTag.GeneralBlockTransfer && options.Association.Security is { } security)
        {
            clear = security.Unprotect(clear.Span, established);
            if (clear.Length > options.Limits.MaxMessageBytes || clear.Length > 0
                && (Enum.IsDefined((AcseKind)clear.Span[0]) || clear.Span[0] == (byte)ApduTag.GeneralBlockTransfer))
            {
                throw new DlmsProtocolException("The protection protocol returned an invalid xDLMS APDU.");
            }
        }

        DecodeResult<DlmsApdu> decoded;
        lock (codecGate)
        {
            decoded = codec.Decode(new ReadOnlySequence<byte>(clear));
        }

        return decoded.Status == DecodeStatus.Done && decoded.Consumed == clear.Length && decoded.Value is { } apdu
            ? apdu : throw new DlmsProtocolException($"The channel did not deliver exactly one supported APDU: {decoded.Status}.");
    }

    private bool Route(DlmsApdu apdu, GeneralConfirmation? confirmation)
    {
        lock (gate)
        {
            if (state is ClientSessionState.Disposed or ClientSessionState.Faulted)
            {
                return true;
            }

            if (apdu is DataNotification notification)
            {
                if (negotiated is { } context
                    && (context.Conformance & DlmsConformance.DataNotification) == DlmsConformance.None)
                {
                    throw new DlmsProtocolException("A DataNotification was received outside the negotiated context.");
                }

                if (notification.IsConfirmed)
                {
                    var repeated = notificationConfirmations.Keys.FirstOrDefault(previous =>
                        previous.LongInvokeIdAndPriority == notification.LongInvokeIdAndPriority
                        && previous.DateTime.AsSpan().SequenceEqual(notification.DateTime.AsSpan()));
                    if (repeated is not null)
                    {
                        notificationConfirmations[repeated] = confirmation;
                        return false;
                    }

                    if (notificationConfirmations.Count >= options.NotificationCapacity)
                    {
                        throw new DlmsProtocolException("The pending notification confirmation limit was reached.");
                    }

                    notificationConfirmations.Add(notification, confirmation);
                }

                if (!notifications.Writer.TryWrite(notification))
                {
                    throw new DlmsProtocolException("The notification buffer limit was reached.");
                }

                return false;
            }

            if (apdu is AcseApdu acse)
            {
                if (control == null || acse.Kind != control.ExpectedKind)
                {
                    throw new DlmsProtocolException("An unexpected ACSE envelope was received.");
                }

                control.Response.TrySetResult(acse);
                control = null;
                return acse.Kind == AcseKind.Rlre;
            }

            lock (generalGate)
            {
                if (generalOutgoing is not null)
                {
                    throw new DlmsProtocolException("The peer ended a general block exchange with a normal APDU.");
                }
            }

            byte invoke = apdu switch
            {
                LnResponse response => response.InvokeIdAndPriority,
                GetBlockResponse block => block.InvokeIdAndPriority,
                SetBlockResponse block => block.InvokeIdAndPriority,
                ActionBlock block => block.InvokeIdAndPriority,
                ActionNextBlock next => next.InvokeIdAndPriority,
                _ => throw new DlmsProtocolException("The peer sent an unexpected application service.")
            };
            byte id = (byte)(invoke & InvokeIdEncoding.IdMask);
            var pending = operations.GetValueOrDefault(id);
            if (pending == null && operations.Count == 1)
            {
                // The sole pending request can accept a mismatched octet in compatibility mode or report the expected octet.
                pending = operations.Values.Single();
            }

            if (pending == null)
            {
                throw new DlmsProtocolException($"No outstanding request matches the received Invoke-Id-And-Priority 0x{invoke:X2}.");
            }

            pending.Deliver(apdu, invoke, state == ClientSessionState.Associating ? HlsActionStage : null);
            return false;
        }
    }
}
