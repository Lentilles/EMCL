using System.Runtime.ExceptionServices;
using System.Threading.Channels;
using EMCL.Client.Association;
using EMCL.Client.Exceptions;
using EMCL.Client.Internal;
using EMCL.Core.Codecs;
using EMCL.Core.Operations;
using EMCL.Protocol.Acse;
using EMCL.Protocol.Apdu;
using EMCL.Protocol.BlockTransfer;
using EMCL.Transport.Channels;

namespace EMCL.Client.Sessions;

/// <summary>Owns one confirmed LN association over an already connected, complete-APDU channel.</summary>
/// <remarks>Operations are serialized by default; opt-in concurrency allocates separate invoke IDs. No retries or transport connections are created. Timeout or cancellation after a send attempt faults the association to prevent late-response reuse. Disposal cancels and drains I/O before optionally disposing the channel. Channel implementations must honor cancellation. Calls to the injected codec are serialized by the session.</remarks>
public sealed partial class DlmsClientSession : IDlmsClientSession
{
    private readonly object gate = new();
    private readonly object codecGate = new();
    private readonly IApduChannel channel;
    private readonly IIncrementalCodec<DlmsApdu> codec;
    private readonly IClientAssociationNegotiator negotiator;
    private readonly ClientSessionOptions options;
    private readonly CancellationTokenSource lifetime = new();
    private readonly CancellationToken lifetimeToken;
    private readonly SemaphoreSlim slots;
    private readonly SemaphoreSlim sendLock = new(1, 1);
    private readonly Dictionary<byte, PendingOperation> operations = [];
    private readonly Channel<DataNotification> notifications;
    private readonly GetBlockAssembler blocks;
    private readonly string exchangeScope = Guid.NewGuid().ToString("N");
    private ClientSessionState state;
    private NegotiatedAssociation? negotiated;
    private Exception? failure;
    private readonly TaskCompletionSource associationReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Task receiveTask = Task.CompletedTask;
    private Task cancellationTask = Task.CompletedTask;
    private Task? disposeTask;
    private PendingControl? control;
    private int activeOperations;
    private int pendingCalls;
    private TaskCompletionSource callsIdle = CompletedSignal();
    private byte nextInvokeId;
    private TaskCompletionSource idle = CompletedSignal();

    /// <summary>Creates a client session without starting channel I/O.</summary>
    /// <param name="channel">The already connected APDU channel, owned unless LeaveOpen is true.</param>
    /// <param name="options">Validated session and association settings; null selects defaults.</param>
    /// <param name="codec">A replaceable complete-APDU codec; null selects the standard codec.</param>
    /// <param name="negotiator">A replaceable ACSE proposal and validation strategy.</param>
    public DlmsClientSession(IApduChannel channel, ClientSessionOptions? options = null,
        IIncrementalCodec<DlmsApdu>? codec = null, IClientAssociationNegotiator? negotiator = null)
    {
        ArgumentNullException.ThrowIfNull(channel);
        this.options = options ?? new();
        this.options.Validate();
        this.channel = channel;
        this.codec = codec ?? new ApduCodec(this.options.Limits);
        this.negotiator = negotiator ?? new LnAssociationNegotiator(this.options.Limits);
        lifetimeToken = lifetime.Token;
        slots = new(this.options.MaxConcurrentRequests, this.options.MaxConcurrentRequests);
        blocks = new(this.options.Limits);
        notifications = Channel.CreateBounded<DataNotification>(new BoundedChannelOptions(this.options.NotificationCapacity)
        {
            SingleWriter = true,
            AllowSynchronousContinuations = false
        });
    }

    /// <inheritdoc />
    public ClientSessionState State
    {
        get
        {
            lock (gate)
            {
                return state;
            }
        }
    }
    /// <inheritdoc />
    public NegotiatedAssociation? NegotiatedParameters
    {
        get
        {
            lock (gate)
            {
                return negotiated;
            }
        }
    }
    /// <summary>Gets the first exchange failure, or null if the session has not faulted.</summary>
    public Exception? Failure
    {
        get
        {
            lock (gate)
            {
                return failure;
            }
        }
    }

    /// <inheritdoc />
    public async ValueTask<NegotiatedAssociation> AssociateAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var request = negotiator.CreateRequest(options.Association);
        if (request.Kind != AcseKind.Aarq)
        {
            throw new ArgumentException("The association negotiator must produce an AARQ.");
        }

        if (options.AssociationRetryDelay is not null && channel is not IAssociationRequestRetransmission)
        {
            throw new NotSupportedException("Association AARQ retry requires a channel with safe link-layer retransmission.");
        }

        using var deadline = new ExchangeDeadline(options, cancellationToken, lifetimeToken);
        string stage = "AARE";
        PendingControl pending;
        lock (gate)
        {
            RequireState(ClientSessionState.Created);
            state = ClientSessionState.Associating;
            control = pending = new(AcseKind.Aare);
            receiveTask = Task.Run(ReceiveLoopAsync);
        }

        try
        {
            await SendAsync(request, options.Limits.MaxMessageBytes, deadline.Token).ConfigureAwait(false);
            if (options.AssociationRetryDelay is { } retryDelay)
            {
                var responseTask = pending.Response.Task;
                var first = await Task.WhenAny(responseTask,
                    Task.Delay(retryDelay, options.TimeProvider, deadline.Token)).ConfigureAwait(false);
                if (first != responseTask && !responseTask.IsCompleted)
                {
                    await ((IAssociationRequestRetransmission)channel)
                        .RetryUnansweredAssociationRequestAsync(deadline.Token).ConfigureAwait(false);
                }
            }

            var response = await pending.Response.Task.WaitAsync(deadline.Token).ConfigureAwait(false);
            var context = negotiator.AcceptResponse(response, options.Association);
            ValidateNegotiatedContext(context);
            lock (gate)
            {
                RequireState(ClientSessionState.Associating);
                negotiated = context;
                associationReady.TrySetResult();
            }

            if (negotiator is IClientHighLevelAuthenticator authenticator)
            {
                var operation = authenticator.CreateAuthenticationOperation(request, response, options.Association);
                if (operation != null)
                {
                    stage = "HLS ACTION";
                    var result = await AuthenticateAsync(operation, deadline.Token).ConfigureAwait(false);
                    authenticator.ValidateAuthenticationResponse(result.Result.Value, request, response, options.Association);
                }
            }

            lock (gate)
            {
                RequireState(ClientSessionState.Associating);
                state = ClientSessionState.Associated;
            }

            return context;
        }
        catch (OperationCanceledException)
        {
            var error = deadline.TranslateCancellation(SessionFailure());
            if (error is TimeoutException timeout)
            {
                error = new TimeoutException($"stage={stage}; association deadline expired.", timeout);
            }
            Fault(error);
            ExceptionDispatchInfo.Capture(error).Throw();
            throw;
        }
        catch (Exception error)
        {
            Fault(error);
            throw;
        }
    }

    private async ValueTask<LnResponse> AuthenticateAsync(CosemOperation operation, CancellationToken cancellationToken)
    {
        PendingOperation pending;
        lock (gate)
        {
            RequireState(ClientSessionState.Associating);
            ValidateOperation(operation);
            pending = RegisterOperation(operation.Kind);
        }

        try
        {
            await SendAsync(new LnRequest(pending.Invoke, operation), SendLimit(), cancellationToken).ConfigureAwait(false);
            var result = await ReceiveOperationAsync(pending, null, cancellationToken, cancellationToken)
                .ConfigureAwait(false);
            ThrowRemoteError(result);
            return result;
        }
        finally
        {
            lock (gate)
            {
                operations.Remove(pending.Id);
                if (--activeOperations == 0)
                {
                    idle.TrySetResult();
                }
            }
        }
    }

    /// <inheritdoc />
    public async ValueTask ReleaseAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Task draining;
        lock (gate)
        {
            if (state == ClientSessionState.Released)
            {
                return;
            }

            RequireState(ClientSessionState.Associated);
        }

        using var deadline = new ExchangeDeadline(options, cancellationToken, lifetimeToken);
        lock (gate)
        {
            RequireState(ClientSessionState.Associated);
            state = ClientSessionState.Releasing;
            draining = idle.Task;
        }

        try
        {
            await draining.WaitAsync(deadline.Token).ConfigureAwait(false);
            var request = negotiator.CreateReleaseRequest();
            if (request.Kind != AcseKind.Rlrq)
            {
                throw new ArgumentException("The association negotiator must produce an RLRQ.");
            }

            PendingControl pending;
            lock (gate)
            {
                RequireState(ClientSessionState.Releasing);
                control = pending = new(AcseKind.Rlre);
            }

            await SendAsync(request, SendLimit(), deadline.Token).ConfigureAwait(false);
            var response = await pending.Response.Task.WaitAsync(deadline.Token).ConfigureAwait(false);
            negotiator.AcceptReleaseResponse(response);
            lock (gate)
            {
                RequireState(ClientSessionState.Releasing);
                state = ClientSessionState.Released;
                notifications.Writer.TryComplete();
            }
        }
        catch (OperationCanceledException)
        {
            var error = deadline.TranslateCancellation(SessionFailure());
            Fault(error);
            ExceptionDispatchInfo.Capture(error).Throw();
            throw;
        }
        catch (Exception error)
        {
            Fault(error);
            throw;
        }
    }

    /// <inheritdoc />
    public IAsyncEnumerable<DataNotification> ReadNotificationsAsync(CancellationToken cancellationToken = default) =>
        notifications.Reader.ReadAllAsync(cancellationToken);

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        TaskCompletionSource completion;
        lock (gate)
        {
            if (disposeTask != null)
            {
                return new(disposeTask);
            }

            state = ClientSessionState.Disposed;
            var error = new ObjectDisposedException(nameof(DlmsClientSession));
            FailPending(error);
            CancelLifetime();
            completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
            disposeTask = completion.Task;
        }

        // Publish the shared disposal task before calling application-owned channel cleanup, outside gate.
        _ = CompleteDisposalAsync(completion);
        return new(completion.Task);
    }

    private async Task CompleteDisposalAsync(TaskCompletionSource completion)
    {
        try
        {
            await DisposeCoreAsync().ConfigureAwait(false);
            completion.TrySetResult();
        }
        catch (Exception error)
        {
            completion.TrySetException(error);
        }
    }

    private async Task DisposeCoreAsync()
    {
        try
        {
            await Task.WhenAll(receiveTask, cancellationTask, callsIdle.Task).ConfigureAwait(false);
        }
        finally
        {
            await sendLock.WaitAsync().ConfigureAwait(false);
            try
            {
                if (!options.LeaveOpen)
                {
                    await channel.DisposeAsync().ConfigureAwait(false);
                }
            }
            finally
            {
                sendLock.Release();
                sendLock.Dispose();
                slots.Dispose();
                lifetime.Dispose();
            }
        }
    }

    private void ValidateNegotiatedContext(NegotiatedAssociation context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.DlmsVersion != DlmsNegotiation.Version6
            || context.ClientMaxReceivePduSize != options.Association.MaxReceivePduSize
            || (context.Conformance & ~options.Association.Conformance) != DlmsConformance.None
            || context.ServerMaxReceivePduSize != DlmsNegotiation.UnlimitedPduSize
                && context.ServerMaxReceivePduSize < DlmsNegotiation.MinimumPduSize
            || options.HighPriority && (context.Conformance & DlmsConformance.PriorityManagement) == DlmsConformance.None)
        {
            throw new DlmsProtocolException("The negotiated context does not match the supported client proposal.");
        }
    }

    private void RequireState(ClientSessionState expected)
    {
        ObjectDisposedException.ThrowIf(state == ClientSessionState.Disposed, this);
        if (state == ClientSessionState.Faulted)
        {
            throw new InvalidOperationException("The client association has faulted; create a new session.", failure);
        }

        if (state != expected)
        {
            throw new InvalidOperationException($"Expected session state {expected}, found {state}.");
        }
    }

    private Exception SessionFailure()
    {
        lock (gate)
        {
            return state == ClientSessionState.Disposed ? new ObjectDisposedException(nameof(DlmsClientSession))
                : failure ?? new IOException("The client association stopped.");
        }
    }

    private void Fault(Exception error)
    {
        lock (gate)
        {
            if (state is ClientSessionState.Faulted or ClientSessionState.Disposed or ClientSessionState.Released)
            {
                return;
            }

            failure = error;
            state = ClientSessionState.Faulted;
            FailPending(error);
            CancelLifetime();
        }
    }

    // Called under gate. CancelAsync avoids invoking user transport cancellation callbacks under the session lock.
    private void CancelLifetime()
    {
        if (!lifetimeToken.IsCancellationRequested)
        {
            cancellationTask = lifetime.CancelAsync();
        }
    }

    private void FailPending(Exception error)
    {
        if (control?.Response.TrySetException(error) == true)
        {
            // A send may fail before the control waiter is reached; the failure is also exposed by Failure.
            _ = control.Response.Task.Exception;
        }

        control = null;
        foreach (var operation in operations.Values)
        {
            operation.Fail(error);
        }

        notifications.Writer.TryComplete(state == ClientSessionState.Disposed ? null : error);
    }

    private int SendLimit() => EffectiveLimit(negotiated!.ServerMaxReceivePduSize);
    private int EffectiveLimit(ushort protocolLimit) => protocolLimit == DlmsNegotiation.UnlimitedPduSize
        ? options.Limits.MaxMessageBytes : Math.Min(protocolLimit, options.Limits.MaxMessageBytes);

    private static TaskCompletionSource CompletedSignal()
    {
        var signal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        signal.SetResult();
        return signal;
    }

    private sealed class PendingControl(AcseKind expectedKind)
    {
        internal AcseKind ExpectedKind { get; } = expectedKind;
        internal TaskCompletionSource<AcseApdu> Response { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
