using EMCL.Core.Codecs;
using EMCL.Client.Internal;
using EMCL.Protocol.Apdu;

namespace EMCL.Client.Sessions;

public sealed partial class DlmsClientSession
{
    private readonly Dictionary<DataNotification, GeneralConfirmation?> notificationConfirmations =
        new(ReferenceEqualityComparer.Instance);
    private int activeConfirmations;
    private TaskCompletionSource confirmationsIdle = CompletedSignal();

    /// <inheritdoc />
    public async ValueTask ConfirmNotificationAsync(DataNotification notification, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(notification);
        cancellationToken.ThrowIfCancellationRequested();
        GeneralConfirmation? general;
        lock (gate)
        {
            RequireState(ClientSessionState.Associated);
            if (!notificationConfirmations.Remove(notification, out general))
            {
                throw new ArgumentException("The notification does not have a pending confirmation in this session.", nameof(notification));
            }

            if (activeConfirmations++ == 0)
            {
                confirmationsIdle = new(TaskCreationOptions.RunContinuationsAsynchronously);
            }
        }

        using var deadline = new ExchangeDeadline(options, cancellationToken, lifetimeToken);
        var token = deadline.Token;
        bool attempted = false;
        try
        {
            var confirmation = new DataNotificationConfirm(notification.LongInvokeIdAndPriority, notification.DateTime);
            if (general is null)
            {
                await SendAsync(confirmation, SendLimit(), token, () => attempted = true).ConfigureAwait(false);
            }
            else
            {
                await protectedSendGate.WaitAsync(token).ConfigureAwait(false);
                try
                {
                    byte[] data;
                    lock (codecGate)
                    {
                        data = codec.EncodeBytes(confirmation);
                    }

                    if (options.Association.Security is { } security)
                    {
                        data = security.Protect(data, true);
                    }

                    await SendAsync(new GeneralBlockTransfer(true, false, 1, general.Number, general.Acknowledged, [.. data]),
                        SendLimit(), token, () => attempted = true).ConfigureAwait(false);
                }
                finally
                {
                    protectedSendGate.Release();
                }
            }
        }
        catch (Exception error)
        {
            if (attempted)
            {
                Fault(error);
            }
            else
            {
                lock (gate)
                {
                    if (state == ClientSessionState.Associated)
                    {
                        notificationConfirmations.TryAdd(notification, general);
                    }
                }
            }

            throw;
        }
        finally
        {
            lock (gate)
            {
                if (--activeConfirmations == 0)
                {
                    confirmationsIdle.TrySetResult();
                }
            }
        }
    }
}
