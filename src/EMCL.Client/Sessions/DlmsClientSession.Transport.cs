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

        if (bytes.Length > maximumBytes)
        {
            throw new ArgumentException("The encoded APDU exceeds the peer or local PDU limit.", nameof(apdu));
        }

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
            onAttempt?.Invoke();
            await channel.SendAsync(bytes, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            sendLock.Release();
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

                DecodeResult<DlmsApdu> decoded;
                lock (codecGate)
                {
                    decoded = codec.Decode(new ReadOnlySequence<byte>(payload.Value));
                }

                if (decoded.Status != DecodeStatus.Done || decoded.Consumed != payload.Value.Length || decoded.Value == null)
                {
                    throw new DlmsProtocolException($"The channel did not deliver exactly one supported APDU: {decoded.Status}.");
                }

                if (Route(decoded.Value))
                {
                    return;
                }

                if (decoded.Value is AcseApdu { Kind: AcseKind.Aare })
                {
                    await associationReady.Task.WaitAsync(lifetimeToken).ConfigureAwait(false);
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

    private bool Route(DlmsApdu apdu)
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

            byte invoke = apdu switch
            {
                LnResponse response => response.InvokeIdAndPriority,
                GetBlockResponse block => block.InvokeIdAndPriority,
                SetBlockResponse block => block.InvokeIdAndPriority,
                _ => throw new DlmsProtocolException("The peer sent an unexpected application service.")
            };
            byte id = (byte)(invoke & InvokeIdEncoding.IdMask);
            if (!operations.TryGetValue(id, out var pending))
            {
                throw new DlmsProtocolException("No outstanding request matches the received invoke ID.");
            }

            pending.Deliver(apdu);
            return false;
        }
    }
}
