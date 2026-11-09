using System.Diagnostics;
using System.Runtime.ExceptionServices;
using EMCL.Framing.Hdlc.Frames;

namespace EMCL.Framing;

public sealed partial class HdlcApduChannel
{
    private readonly int retryCount;
    private readonly TimeSpan acknowledgementTimeout;
    private readonly CancellationTokenSource? recoveryLifetime;
    private readonly CancellationToken recoveryToken;
    private Task? recoveryTask;
    private Exception? recoveryFailure;

    // Numbered receive, initial send, and recovery all update this state under writeLock.
    private PendingInformation? pendingInformation;
    private TaskCompletionSource pendingChanged = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private sealed class PendingInformation(HdlcFrame frame, CancellationToken cancellationToken)
    {
        public HdlcFrame Frame { get; } = frame;
        public CancellationToken CancellationToken { get; } = cancellationToken;
        public long LastTransmission { get; set; } = Stopwatch.GetTimestamp();
        public int Attempts
        {
            get; set;
        }
        public bool AwaitingPollResponse
        {
            get; set;
        }
    }

    private CancellationTokenSource? LinkRecoveryToken(CancellationToken cancellationToken) =>
        recoveryLifetime is null ? null : CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, recoveryToken);

    private void EnsureRecoveryAvailable()
    {
        if (Volatile.Read(ref recoveryFailure) is { } error)
        {
            ExceptionDispatchInfo.Throw(error);
        }
    }

    private async ValueTask StopRecoveryAsync(Exception error)
    {
        RecordRecoveryFailure(error);
        await recoveryLifetime!.CancelAsync().ConfigureAwait(false);
    }

    private void RecordRecoveryFailure(Exception error) =>
        Interlocked.CompareExchange(ref recoveryFailure, error, null);

    private void SignalPendingChanged()
    {
        var previous = pendingChanged;
        pendingChanged = new(TaskCreationOptions.RunContinuationsAsynchronously);
        previous.SetResult();
    }

    private void AcknowledgePendingFrame()
    {
        if (pendingInformation is not null)
        {
            pendingInformation = null;
            SignalPendingChanged();
        }
    }

    private async Task RunRecoveryAsync()
    {
        try
        {
            while (true)
            {
                Task changed;
                TimeSpan delay = Timeout.InfiniteTimeSpan;
                CancellationToken exchangeToken = default;
                await writeLock.WaitAsync(recoveryToken).ConfigureAwait(false);
                try
                {
                    recoveryToken.ThrowIfCancellationRequested();
                    EnsureLinkAvailable();
                    if (pendingInformation is { } pending)
                    {
                        exchangeToken = pending.CancellationToken;
                        exchangeToken.ThrowIfCancellationRequested();
                        delay = acknowledgementTimeout - Stopwatch.GetElapsedTime(pending.LastTransmission);
                        if (delay <= TimeSpan.Zero)
                        {
                            await PollPeerAsync(pending).ConfigureAwait(false);
                            delay = acknowledgementTimeout;
                        }
                    }
                    else if (receiveResponseWait is { } response)
                    {
                        exchangeToken = response.CancellationToken;
                        exchangeToken.ThrowIfCancellationRequested();
                        delay = acknowledgementTimeout - Stopwatch.GetElapsedTime(response.LastActivity);
                        if (delay <= TimeSpan.Zero)
                        {
                            await PollReceiveResponseAsync(response).ConfigureAwait(false);
                            delay = acknowledgementTimeout;
                        }
                    }

                    changed = pendingChanged.Task;
                }
                catch (Exception error) when (!recoveryToken.IsCancellationRequested)
                {
                    RecordRecoveryFailure(error);
                    throw;
                }
                finally
                {
                    writeLock.Release();
                }

                // Wait without canceling the transport read. On Serial, canceling a read closes the port.
                using var wait = CancellationTokenSource.CreateLinkedTokenSource(recoveryToken, exchangeToken);
                try
                {
                    await changed.WaitAsync(delay, wait.Token).ConfigureAwait(false);
                }
                catch (TimeoutException)
                {
                    // Recheck the current frame and deadline under the same lock used for acknowledgements.
                }
                catch (OperationCanceledException) when (!recoveryToken.IsCancellationRequested)
                {
                    // The canceled exchange may already have been acknowledged.
                }
            }
        }
        catch (OperationCanceledException) when (recoveryToken.IsCancellationRequested)
        {
            // Disposal or a receive/send failure stopped recovery.
        }
        catch (Exception error)
        {
            await StopRecoveryAsync(error).ConfigureAwait(false);
        }
    }

    private async Task PollPeerAsync(PendingInformation pending)
    {
        if (pending.Attempts == retryCount)
        {
            throw new TimeoutException($"stage=HDLC acknowledgement; I-frame N(S)={pending.Frame.SendSequence} "
                + $"remained unacknowledged after {pending.Attempts} recovery attempt(s). Establish a new HDLC connection.");
        }

        pending.Attempts++;
        pending.AwaitingPollResponse = true;
        // Green Book 8.4.5.5.1: poll with RR to resynchronize numbering after an I-frame times out.
        var poll = new HdlcFrame(remote, local, HdlcControl.CreateReceiveReady(nextReceiveSequence), []);
        await WriteRecoveryFrameAsync(poll, pending,
            new HdlcLinkEvent.RecoveryPoll(pending.Frame.SendSequence, pending.Attempts), recoveryToken).ConfigureAwait(false);
        receiveFramesSinceAcknowledgement = 0;
    }

    private async Task RetransmitAsync(PendingInformation pending, CancellationToken cancellationToken)
    {
        pending.AwaitingPollResponse = false;
        byte control = (byte)(pending.Frame.SendSequence << HdlcFrameFormat.SendSequenceShift
            | nextReceiveSequence << HdlcFrameFormat.ReceiveSequenceShift | HdlcFrameFormat.PollFinalMask);
        await WriteRecoveryFrameAsync(pending.Frame with
        {
            Control = control
        }, pending,
            new HdlcLinkEvent.Retransmission(pending.Frame.SendSequence, pending.Attempts), cancellationToken).ConfigureAwait(false);
        SignalPendingChanged();
    }

    private async Task WriteRecoveryFrameAsync(HdlcFrame frame, PendingInformation pending, HdlcLinkEvent diagnostic,
        CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, pending.CancellationToken);
        deadline.CancelAfter(acknowledgementTimeout);
        deadline.Token.ThrowIfCancellationRequested();
        linkObserver?.Invoke(diagnostic);
        try
        {
            await WriteFrameCoreAsync(frame, deadline.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException error) when (deadline.IsCancellationRequested
            && !cancellationToken.IsCancellationRequested && !pending.CancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("stage=HDLC acknowledgement; the recovery frame could not be sent in time.", error);
        }

        pending.LastTransmission = Stopwatch.GetTimestamp();
    }

    private async Task WriteReceiveReadyAsync(CancellationToken cancellationToken)
    {
        await WriteFrameCoreAsync(new(remote, local, HdlcControl.CreateReceiveReady(nextReceiveSequence), []), cancellationToken)
            .ConfigureAwait(false);
        receiveFramesSinceAcknowledgement = 0;
        if (Negotiation.Effective.ReceiveWindowFrames > 1 && (adapter!.HasPending || receiveSequenceError || awaitingReceiveFinal))
        {
            WaitForReceiveFinal(progress: false);
        }
    }
}
