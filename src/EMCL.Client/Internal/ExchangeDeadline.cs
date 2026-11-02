using EMCL.Client.Sessions;

namespace EMCL.Client.Internal;

internal sealed class ExchangeDeadline : IDisposable
{
    private readonly CancellationTokenSource timeout;
    private readonly CancellationTokenSource processing;
    private readonly CancellationTokenSource linked;
    private readonly CancellationToken caller;

    internal ExchangeDeadline(ClientSessionOptions options, CancellationToken caller, CancellationToken lifetime)
    {
        this.caller = caller;
        timeout = new(options.RequestTimeout, options.TimeProvider);
        processing = CancellationTokenSource.CreateLinkedTokenSource(caller, timeout.Token);
        linked = CancellationTokenSource.CreateLinkedTokenSource(processing.Token, lifetime);
    }

    internal CancellationToken Token => linked.Token;

    // A fully received response still honors the caller and deadline after the peer closes.
    internal CancellationToken ProcessingToken => processing.Token;

    internal Exception TranslateCancellation(Exception sessionFailure)
    {
        if (caller.IsCancellationRequested)
        {
            return new OperationCanceledException(caller);
        }

        return timeout.IsCancellationRequested ? new TimeoutException("The DLMS exchange deadline expired.") : sessionFailure;
    }

    public void Dispose()
    {
        linked.Dispose();
        processing.Dispose();
        timeout.Dispose();
    }
}
