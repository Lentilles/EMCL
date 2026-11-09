using System.Diagnostics;
using EMCL.Framing.Hdlc.Frames;

namespace EMCL.Framing;

public sealed partial class HdlcApduChannel
{
    // Numbered receive and recovery update state under writeLock; only the reader drains completed APDUs.
    private readonly Queue<HdlcFrame> recentReceiveFrames = new();
    private readonly Queue<ReadOnlyMemory<byte>> completedWindowApdus = new();
    private int receiveFramesSinceAcknowledgement;
    private bool receiveSequenceError;
    private bool awaitingReceiveFinal;
    private CancellationToken receiveExchangeToken;
    private ReceiveResponseWait? receiveResponseWait;

    private sealed class ReceiveResponseWait(CancellationToken cancellationToken)
    {
        public CancellationToken CancellationToken { get; } = cancellationToken;
        public long LastActivity { get; set; } = Stopwatch.GetTimestamp();
        public int Attempts
        {
            get; set;
        }
    }

    private async ValueTask<ReadOnlyMemory<byte>?> AcceptWindowedInformationFrameAsync(HdlcApduAdapter dataAdapter,
        HdlcFrame frame, CancellationToken cancellationToken)
    {
        bool final = (frame.Control & HdlcFrameFormat.PollFinalMask) != 0;
        bool duplicate = frame.SendSequence != nextReceiveSequence && recentReceiveFrames.Any(previous =>
            frame.SendSequence == previous.SendSequence && frame.Segmented == previous.Segmented
            && frame.Information.AsSpan().SequenceEqual(previous.Information.AsSpan()));
        if (duplicate || receiveSequenceError || frame.SendSequence != nextReceiveSequence)
        {
            if (!duplicate)
            {
                receiveSequenceError = true;
                linkObserver?.Invoke(new HdlcLinkEvent.ReceiveSequenceError(nextReceiveSequence, frame.SendSequence));
            }

            WaitForReceiveFinal(progress: false);
            if (final)
            {
                // Poll/final recovery: retain the accepted prefix and request the next missing sequence.
                receiveSequenceError = false;
                await WriteReceiveReadyAsync(cancellationToken).ConfigureAwait(false);
            }

            return null;
        }

        bool acknowledges = frame.ReceiveSequence == nextSendSequence;
        if (!acknowledges && pendingInformation?.Frame.SendSequence != frame.ReceiveSequence)
        {
            FailLink("Unexpected HDLC I-frame acknowledgement in a receive window");
        }

        if (receiveFramesSinceAcknowledgement >= Negotiation.Effective.ReceiveWindowFrames)
        {
            FailLink("The peer exceeded the negotiated HDLC receive window in frames");
        }

        if (acknowledges)
        {
            AcknowledgePendingFrame();
        }

        receiveFramesSinceAcknowledgement++;
        recentReceiveFrames.Enqueue(frame);
        while (recentReceiveFrames.Count > Negotiation.Effective.ReceiveWindowFrames)
        {
            recentReceiveFrames.Dequeue();
        }

        nextReceiveSequence = (byte)((nextReceiveSequence + 1) & HdlcFrameFormat.SequenceMask);
        if (dataAdapter.Accept(frame with { Source = remote }) is { } apdu)
        {
            completedWindowApdus.Enqueue(apdu);
        }

        WaitForReceiveFinal(progress: true);
        if (!final)
        {
            return null;
        }

        if (frame.Segmented)
        {
            // A peer may finish its turn before filling the negotiated window.
            await WriteReceiveReadyAsync(cancellationToken).ConfigureAwait(false);
            return null;
        }

        CompleteReceiveTurn();
        return completedWindowApdus.TryDequeue(out var completed) ? completed : null;
    }

    private void WaitForReceiveFinal(bool progress)
    {
        awaitingReceiveFinal = true;
        if (recoveryLifetime is null)
        {
            return;
        }

        receiveResponseWait ??= new(receiveExchangeToken);
        receiveResponseWait.LastActivity = Stopwatch.GetTimestamp();
        if (progress)
        {
            receiveResponseWait.Attempts = 0;
        }

        SignalPendingChanged();
    }

    private void CompleteReceiveTurn()
    {
        awaitingReceiveFinal = false;
        receiveResponseWait = null;
        SignalPendingChanged();
    }

    private void ObserveReceiveFinal(HdlcFrame frame)
    {
        if (Negotiation.Effective.ReceiveWindowFrames <= 1)
        {
            return;
        }

        if ((frame.Control & HdlcFrameFormat.PollFinalMask) == 0)
        {
            WaitForReceiveFinal(progress: false);
        }
        else if (adapter!.HasPending || receiveSequenceError)
        {
            // RR/F can end a turn without completing the segmented response. Retry only after the timer.
            awaitingReceiveFinal = true;
            if (receiveResponseWait is not null)
            {
                receiveResponseWait.LastActivity = Stopwatch.GetTimestamp();
            }

            SignalPendingChanged();
        }
        else
        {
            CompleteReceiveTurn();
        }
    }

    private async Task ObserveWindowedUiFrameAsync(HdlcFrame frame, CancellationToken cancellationToken)
    {
        if (Negotiation.Effective.ReceiveWindowFrames <= 1)
        {
            return;
        }

        await writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!awaitingReceiveFinal)
            {
                return;
            }

            if ((frame.Control & HdlcFrameFormat.PollFinalMask) != 0 && (adapter!.HasPending || receiveSequenceError))
            {
                receiveSequenceError = false;
                await WriteReceiveReadyAsync(cancellationToken).ConfigureAwait(false);
            }
            else
            {
                ObserveReceiveFinal(frame);
            }
        }
        finally
        {
            writeLock.Release();
        }
    }

    private async Task ObserveWindowedSupervisoryFrameAsync(HdlcFrame frame, CancellationToken cancellationToken)
    {
        if (Negotiation.Effective.ReceiveWindowFrames > 1 && receiveSequenceError && pendingInformation is null
            && (frame.Control & HdlcFrameFormat.PollFinalMask) != 0)
        {
            receiveSequenceError = false;
            await WriteReceiveReadyAsync(cancellationToken).ConfigureAwait(false);
        }
        else
        {
            ObserveReceiveFinal(frame);
        }
    }

    private async Task PollReceiveResponseAsync(ReceiveResponseWait pending)
    {
        if (pending.Attempts == retryCount)
        {
            throw new TimeoutException($"stage=HDLC receive; response remained incomplete after {pending.Attempts} recovery attempt(s). Establish a new HDLC connection.");
        }

        pending.Attempts++;
        linkObserver?.Invoke(new HdlcLinkEvent.ReceiveRecoveryPoll(nextReceiveSequence, pending.Attempts));
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(recoveryToken, pending.CancellationToken);
        deadline.CancelAfter(acknowledgementTimeout);
        receiveSequenceError = false;
        try
        {
            await WriteReceiveReadyAsync(deadline.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException error) when (deadline.IsCancellationRequested
            && !recoveryToken.IsCancellationRequested && !pending.CancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("stage=HDLC receive; the recovery poll could not be sent in time.", error);
        }
    }
}
