using System.Buffers;
using EMCL.Core.Codecs;
using EMCL.Framing.Hdlc.Frames;
using EMCL.Framing.Hdlc.Llc;
using EMCL.Protocol.Acse;
using EMCL.Protocol.Apdu;
using EMCL.Transport.Channels;
using EMCL.Transport.Streams;

namespace EMCL.Framing;

/// <summary>Maintains a client HDLC link and exchanges complete APDUs over an application-supplied byte transport.</summary>
/// <remarks>Uses stop-and-wait transmission, negotiated receive windows of one through seven frames, SNRM/UA setup,
/// receive-ready acknowledgements, and DISC on disposal.
/// Outgoing APDUs must fit one negotiated information frame. Automatic recovery of unacknowledged I-frames is opt-in.
/// A bounded number of SNRM/UA duplicates are tolerated before the first data response.
/// An initial empty UA may be followed by one UA with valid negotiation parameters.
/// A later UA makes the link state uncertain and requires a new connection.
/// For RX windows above one, keep receiving while a concurrent send waits for acknowledgement or the end of the peer's turn.
/// Use one reader at a time and dispose after active operations complete.</remarks>
public sealed partial class HdlcApduChannel : IApduChannel, IAssociationRequestRetransmission
{
    private const int MaximumStaleFramesDuringLinkSetup = 32;
    private const int MaximumDuplicateUaFrames = 32;
    private const int DiagnosticPrefixLength = 16;
    private static readonly TimeSpan DisconnectTimeout = TimeSpan.FromSeconds(2);
    private readonly IByteTransport transport;
    private readonly HdlcCodec codec;
    private readonly HdlcAddress local;
    private readonly HdlcAddress remote;
    private readonly HdlcAddress? notificationAddress;
    private readonly Action<HdlcFrame, bool>? frameObserver;
    private readonly Action<HdlcLinkEvent>? linkObserver;
    private readonly HdlcLinkParameters? proposedParameters;
    private readonly Action<HdlcNegotiationResult>? negotiationObserver;
    private HdlcNegotiationResult? negotiation;
    private int maximumSetupInformationSent;
    private readonly SemaphoreSlim writeLock = new(1, 1);
    private readonly byte[] input = new byte[HdlcFrameFormat.MaximumFrameLength + HdlcFrameFormat.BoundaryFlagsLength];
    private readonly Queue<HdlcFrame> setupNotifications = new();
    private HdlcApduAdapter? adapter;
    private HdlcFrame? setupUa;
    private HdlcFrame? firstDataFrame;
    private HdlcFrame? lastReceivedInformation;
    private bool associationRetryUsed;
    private string? linkFailure;
    private bool awaitingFirstDataResponse = true;
    private int duplicateUaFrames;
    private int inputCount;
    private bool resynchronizing;
    private int disposed;
    private byte nextReceiveSequence;
    private byte nextSendSequence;

    private HdlcApduChannel(IByteTransport transport, ApduFraming.Hdlc framing)
    {
        this.transport = transport;
        local = framing.Local;
        remote = framing.Remote;
        notificationAddress = framing.NotificationAddress;
        frameObserver = framing.FrameObserver;
        linkObserver = framing.LinkObserver;
        proposedParameters = framing.LinkParameters;
        negotiationObserver = framing.NegotiationObserver;
        retryCount = framing.InformationFrameRetryCount;
        acknowledgementTimeout = framing.AcknowledgementTimeout;
        if (retryCount > 0)
        {
            recoveryLifetime = new();
            recoveryToken = recoveryLifetime.Token;
        }

        codec = new()
        {
            AllowMissingClosingFlag = framing.AllowMissingClosingFlag
        };
    }

    /// <summary>Gets the latest accepted SNRM/UA snapshot. Effective directions are local; received directions are the peer's.
    /// The snapshot can change when a parameterized UA replaces an empty UA before the first data response.</summary>
    public HdlcNegotiationResult Negotiation => Volatile.Read(ref negotiation)
        ?? throw new InvalidOperationException("The HDLC link is not established.");

    /// <summary>Establishes an HDLC link and returns a channel that owns the supplied transport.</summary>
    /// <param name="transport">An already open byte transport.</param>
    /// <param name="local">Local HDLC client address.</param>
    /// <param name="remote">Remote HDLC server address.</param>
    /// <param name="timeout">Maximum time allowed for SNRM/UA setup.</param>
    /// <param name="limits">Resource bounds for assembled APDUs.</param>
    /// <param name="frameObserver">Optional callback called for transmitted (false) and received (true) frames.</param>
    /// <param name="cancellationToken">Cancels link setup.</param>
    public static Task<HdlcApduChannel> ConnectAsync(IByteTransport transport, HdlcAddress local,
        HdlcAddress remote, TimeSpan timeout, ProtocolLimits? limits = null,
        Action<HdlcFrame, bool>? frameObserver = null, CancellationToken cancellationToken = default) =>
        ConnectAsync(transport, new ApduFraming.Hdlc(local, remote, timeout, frameObserver), limits, cancellationToken);

    /// <summary>Establishes an HDLC link with the supplied framing settings and takes ownership of the transport.</summary>
    /// <param name="transport">An already open byte transport.</param>
    /// <param name="framing">The addresses, setup timeout, and receive compatibility settings.</param>
    /// <param name="limits">Resource bounds for assembled APDUs.</param>
    /// <param name="cancellationToken">Cancels link setup.</param>
    public static Task<HdlcApduChannel> ConnectAsync(IByteTransport transport, ApduFraming.Hdlc framing,
        ProtocolLimits? limits = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(framing);
        return ConnectCoreAsync(transport, framing, limits, cancellationToken);
    }

    private static async Task<HdlcApduChannel> ConnectCoreAsync(IByteTransport transport, ApduFraming.Hdlc framing,
        ProtocolLimits? limits, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(transport);
        framing.Validate();

        var channel = new HdlcApduChannel(transport, framing);
        var local = framing.Local;
        var remote = framing.Remote;
        try
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(framing.LinkTimeout);
            try
            {
                await channel.WriteFrameAsync(new(remote, local, HdlcControl.Snrm,
                    HdlcNegotiation.CreateSnrmInformation(framing.LinkParameters)), deadline.Token);
                var response = await channel.WaitForUaAsync(deadline.Token);

                if (response.Segmented)
                {
                    throw new InvalidDataException("HDLC UA must not be segmented.");
                }

                var result = HdlcNegotiation.Negotiate(framing.LinkParameters, response.Information);
                channel.setupUa = response;
                channel.adapter = new(local, remote, maxInformationBytes: result.Effective.MaximumTransmitInformationBytes, limits: limits);
                channel.SetNegotiation(result);
                if (channel.recoveryLifetime is not null)
                {
                    channel.recoveryTask = channel.RunRecoveryAsync();
                }

                return channel;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException($"stage=SNRM/UA; HDLC link setup timed out; client={local.Value}/{local.Length}, server={remote.Value}/{remote.Length}.");
            }
        }
        catch
        {
            await channel.DisposeAsync();
            throw;
        }
    }

    /// <inheritdoc />
    public async ValueTask<ReadOnlyMemory<byte>?> ReceiveAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        EnsureLinkAvailable();
        using var linked = LinkRecoveryToken(cancellationToken);
        try
        {
            return await ReceiveCoreAsync(linked?.Token ?? cancellationToken).ConfigureAwait(false);
        }
        catch (Exception error) when (recoveryLifetime is not null)
        {
            await StopRecoveryAsync(error).ConfigureAwait(false);
            if (!cancellationToken.IsCancellationRequested)
            {
                EnsureRecoveryAvailable();
            }
            throw;
        }
        catch (Exception) when (Negotiation.Effective.ReceiveWindowFrames > 1)
        {
            await writeLock.WaitAsync(CancellationToken.None).ConfigureAwait(false);
            try
            {
                linkFailure ??= "stage=HDLC receive; the windowed receive failed. Establish a new HDLC connection.";
                SignalPendingChanged();
            }
            finally
            {
                writeLock.Release();
            }

            throw;
        }
    }

    private async ValueTask<ReadOnlyMemory<byte>?> ReceiveCoreAsync(CancellationToken cancellationToken)
    {
        var dataAdapter = adapter ?? throw new InvalidOperationException("The HDLC link is not established.");
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!awaitingReceiveFinal && completedWindowApdus.TryDequeue(out var completed))
            {
                return completed;
            }

            var frame = setupNotifications.TryDequeue(out var buffered)
                ? buffered
                : await ReadFrameAsync(cancellationToken).ConfigureAwait(false);
            ValidateAddresses(frame);
            if ((frame.IsInformation || frame.IsUnnumberedInformation)
                && frame.Information.Length > Negotiation.Effective.MaximumReceiveInformationBytes)
            {
                FailLink($"HDLC information field contains {frame.Information.Length} bytes, exceeding the negotiated receive limit of {Negotiation.Effective.MaximumReceiveInformationBytes} bytes");
            }

            if (frame.IsUnnumberedInformation)
            {
                await ObserveWindowedUiFrameAsync(frame, cancellationToken).ConfigureAwait(false);
                // UI traffic is independent of the numbered exchange, including AARQ retry state.
                return dataAdapter.Accept(frame with
                {
                    Destination = local,
                    Source = remote
                });
            }

            if (frame.Control == HdlcControl.Ua)
            {
                await HandleDuplicateUaAsync(frame, cancellationToken).ConfigureAwait(false);
                continue;
            }

            await writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                EnsureLinkAvailable();
                awaitingFirstDataResponse = false;
                if (frame.Control is HdlcControl.Dm or HdlcControl.Frmr or HdlcControl.Disc or HdlcControl.Snrm)
                {
                    FailLink($"Unexpected HDLC link-control frame {frame.Control:X2} during data exchange");
                }

                if (frame.IsInformation)
                {
                    if (await AcceptInformationFrameAsync(dataAdapter, frame, cancellationToken).ConfigureAwait(false) is { } apdu)
                    {
                        return apdu;
                    }
                }
                else
                {
                    await AcceptSupervisoryFrameAsync(frame, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (Exception error) when (recoveryLifetime is not null)
            {
                RecordRecoveryFailure(error);
                throw;
            }
            finally
            {
                writeLock.Release();
            }
        }
    }

    private async Task HandleDuplicateUaAsync(HdlcFrame frame, CancellationToken cancellationToken)
    {
        if (await TryAcceptSetupUaAsync(frame, cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        string state = awaitingFirstDataResponse
            ? "while awaiting the first data response after SNRM/UA"
            : "after another frame was received during data exchange";
        FailLink($"Unexpected HDLC UA {state}");
    }

    private async ValueTask<ReadOnlyMemory<byte>?> AcceptInformationFrameAsync(HdlcApduAdapter dataAdapter,
        HdlcFrame frame, CancellationToken cancellationToken)
    {
        if (Negotiation.Effective.ReceiveWindowFrames > 1)
        {
            return await AcceptWindowedInformationFrameAsync(dataAdapter, frame, cancellationToken).ConfigureAwait(false);
        }

        if (lastReceivedInformation is { } previous && frame.SendSequence == previous.SendSequence
            && frame.Segmented == previous.Segmented && frame.Information.AsSpan().SequenceEqual(previous.Information.AsSpan()))
        {
            if (frame.Segmented)
            {
                await WriteReceiveReadyAsync(cancellationToken).ConfigureAwait(false);
            }

            return null;
        }

        bool acknowledges = frame.ReceiveSequence == nextSendSequence;
        if (frame.SendSequence != nextReceiveSequence
            || !acknowledges && pendingInformation?.Frame.SendSequence != frame.ReceiveSequence)
        {
            throw new InvalidDataException("Unexpected HDLC I-frame sequence or acknowledgement.");
        }

        if (acknowledges)
        {
            AcknowledgePendingFrame();
        }

        lastReceivedInformation = frame;
        nextReceiveSequence = (byte)((nextReceiveSequence + 1) & HdlcFrameFormat.SequenceMask);
        var apdu = dataAdapter.Accept(frame with
        {
            Source = remote
        });
        if (frame.Segmented)
        {
            await WriteReceiveReadyAsync(cancellationToken).ConfigureAwait(false);
        }

        return apdu;
    }

    private async ValueTask AcceptSupervisoryFrameAsync(HdlcFrame frame, CancellationToken cancellationToken)
    {
        await AcceptSupervisoryFrameCoreAsync(frame, cancellationToken).ConfigureAwait(false);
        await ObserveWindowedSupervisoryFrameAsync(frame, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask AcceptSupervisoryFrameCoreAsync(HdlcFrame frame, CancellationToken cancellationToken)
    {
        switch (frame.Control & HdlcControl.SupervisoryTypeMask)
        {
            case HdlcControl.ReceiveReady when frame.ReceiveSequence == nextSendSequence:
                AcknowledgePendingFrame();
                return;
            case HdlcControl.ReceiveReady when pendingInformation is { } pending
                && frame.ReceiveSequence == pending.Frame.SendSequence:
                if (pending.AwaitingPollResponse && (frame.Control & HdlcFrameFormat.PollFinalMask) != 0)
                {
                    await RetransmitAsync(pending, cancellationToken).ConfigureAwait(false);
                }

                return;
            case HdlcControl.ReceiveReady:
                throw new InvalidDataException("Unexpected HDLC RR acknowledgement.");
            case HdlcControl.ReceiveNotReady:
                throw new IOException("The meter reported HDLC receive-not-ready (RNR).");
            case HdlcControl.Reject:
                throw new IOException("The meter rejected an HDLC information frame (REJ).");
            default:
                throw new InvalidDataException($"Unexpected HDLC control {frame.Control:X2}.");
        }
    }

    /// <inheritdoc />
    public async ValueTask SendAsync(ReadOnlyMemory<byte> apdu, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        EnsureLinkAvailable();
        using var linked = LinkRecoveryToken(cancellationToken);
        var sendToken = linked?.Token ?? cancellationToken;
        bool startedWrite = false;
        try
        {
            while (true)
            {
                Task changed;
                await writeLock.WaitAsync(sendToken).ConfigureAwait(false);
                try
                {
                    EnsureLinkAvailable();
                    if (pendingInformation is null && !awaitingReceiveFinal)
                    {
                        var frame = FrameApdu(apdu);
                        startedWrite = true;
                        await WriteFrameCoreAsync(frame, sendToken).ConfigureAwait(false);
                        TrackInformationFrame(frame, apdu, cancellationToken);
                        return;
                    }

                    changed = pendingChanged.Task;
                }
                catch (Exception error) when (startedWrite && recoveryLifetime is not null)
                {
                    RecordRecoveryFailure(error);
                    throw;
                }
                finally
                {
                    writeLock.Release();
                }

                await changed.WaitAsync(sendToken).ConfigureAwait(false);
            }
        }
        catch (Exception error) when (recoveryLifetime is not null)
        {
            if (startedWrite)
            {
                await StopRecoveryAsync(error).ConfigureAwait(false);
            }

            if (!cancellationToken.IsCancellationRequested)
            {
                EnsureRecoveryAvailable();
            }

            throw;
        }
    }

    private HdlcFrame FrameApdu(ReadOnlyMemory<byte> apdu)
    {
        var dataAdapter = adapter ?? throw new InvalidOperationException("The HDLC link is not established.");
        dataAdapter.ReceiveSequence = nextReceiveSequence;
        byte previousSendSequence = dataAdapter.NextSendSequence;
        var frames = dataAdapter.Frame(apdu).ToArray();
        if (frames.Length != 1)
        {
            dataAdapter.NextSendSequence = previousSendSequence;
            throw new NotSupportedException("The HDLC channel requires an outgoing APDU to fit one negotiated information frame.");
        }

        nextSendSequence = dataAdapter.NextSendSequence;
        return frames[0];
    }

    private void TrackInformationFrame(HdlcFrame frame, ReadOnlyMemory<byte> apdu, CancellationToken cancellationToken)
    {
        receiveExchangeToken = cancellationToken;
        receiveFramesSinceAcknowledgement = 0;
        if (awaitingFirstDataResponse)
        {
            maximumSetupInformationSent = Math.Max(maximumSetupInformationSent, frame.Information.Length);
        }

        if (recoveryLifetime is not null || Negotiation.Effective.ReceiveWindowFrames > 1)
        {
            pendingInformation = new(frame, cancellationToken);
            SignalPendingChanged();
        }

        if (recoveryLifetime is null && nextSendSequence == 1 && awaitingFirstDataResponse && firstDataFrame is null
            && !apdu.IsEmpty && apdu.Span[0] == (byte)AcseKind.Aarq)
        {
            firstDataFrame = frame;
        }
    }

    bool IAssociationRequestRetransmission.HandlesAssociationRetransmission => recoveryLifetime is not null;

    /// <inheritdoc />
    public async ValueTask<bool> RetryUnansweredAssociationRequestAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        EnsureLinkAvailable();
        await writeLock.WaitAsync(cancellationToken);
        try
        {
            if (recoveryLifetime is not null || associationRetryUsed || !awaitingFirstDataResponse || nextReceiveSequence != 0
                || nextSendSequence != 1 || firstDataFrame is null)
            {
                return false;
            }

            associationRetryUsed = true;
            await WriteFrameCoreAsync(firstDataFrame, cancellationToken);
            return true;
        }
        finally
        {
            writeLock.Release();
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        if (recoveryLifetime is not null)
        {
            await recoveryLifetime.CancelAsync().ConfigureAwait(false);
            if (recoveryTask is not null)
            {
                await recoveryTask.ConfigureAwait(false);
            }

            recoveryLifetime.Dispose();
        }

        if (adapter != null)
        {
            using var deadline = new CancellationTokenSource(DisconnectTimeout);
            try
            {
                await WriteFrameAsync(new(remote, local, HdlcControl.Disc, []), deadline.Token);
            }
            catch (Exception error) when (error is IOException or OperationCanceledException or ObjectDisposedException)
            {
                // The peer may have closed first; local disposal still succeeds.
            }
        }

        writeLock.Dispose();
        await transport.DisposeAsync();
    }

    private async Task<HdlcFrame> WaitForUaAsync(CancellationToken cancellationToken)
    {
        for (int staleFrames = 0; staleFrames <= MaximumStaleFramesDuringLinkSetup; staleFrames++)
        {
            var frame = await ReadFrameAsync(cancellationToken);
            if (!DestinationMatches(frame) || !ServerMatches(frame.Source))
            {
                // Some gateways flush frames from a previous client when a new connection opens.
                continue;
            }

            if (frame.Control == HdlcControl.Ua)
            {
                return frame;
            }

            if (frame.Control == HdlcControl.Dm)
            {
                throw new IOException($"stage=SNRM/UA; the meter rejected HDLC link establishment (DM); client={local.Value}/{local.Length}, server={remote.Value}/{remote.Length}.");
            }

            if (IsDataNotificationFrame(frame))
            {
                setupNotifications.Enqueue(frame);
            }
        }

        throw new InvalidDataException($"stage=SNRM/UA; no matching HDLC UA after SNRM; stale frames exceeded the limit; client={local.Value}/{local.Length}, server={remote.Value}/{remote.Length}.");
    }

    private async Task<bool> TryAcceptSetupUaAsync(HdlcFrame frame, CancellationToken cancellationToken)
    {
        await writeLock.WaitAsync(cancellationToken);
        try
        {
            if (!awaitingFirstDataResponse || nextReceiveSequence != 0 || nextSendSequence > 1
                || frame.Segmented || duplicateUaFrames >= MaximumDuplicateUaFrames)
            {
                return false;
            }

            if (frame.Information.AsSpan().SequenceEqual(setupUa!.Information.AsSpan()))
            {
                duplicateUaFrames++;
                return true;
            }

            // Some peers send a bare UA followed by the negotiated SNRM response.
            if (!setupUa.Information.IsEmpty || frame.Information.IsEmpty)
            {
                return false;
            }

            HdlcNegotiationResult result;
            try
            {
                result = HdlcNegotiation.Negotiate(proposedParameters, frame.Information);
            }
            catch (InvalidDataException)
            {
                return false;
            }

            if (maximumSetupInformationSent > result.Effective.MaximumTransmitInformationBytes)
            {
                return false;
            }

            adapter!.SetMaximumInformationBytes(result.Effective.MaximumTransmitInformationBytes);
            setupUa = frame;
            duplicateUaFrames++;
            SetNegotiation(result);
            return true;
        }
        finally
        {
            writeLock.Release();
        }
    }

    private void SetNegotiation(HdlcNegotiationResult result)
    {
        Volatile.Write(ref negotiation, result);
        negotiationObserver?.Invoke(result);
    }

    private async Task<HdlcFrame> ReadFrameAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            if (TryDecodeBufferedFrame() is { } frame)
            {
                return frame;
            }

            if (inputCount > 1 && input.AsSpan(0, inputCount).IndexOfAnyExcept(HdlcFrameFormat.Flag) < 0)
            {
                // Keep the opening flag when the buffer contains only repeated flags.
                inputCount = 1;
            }

            if (inputCount == input.Length)
            {
                throw new InvalidDataException("HDLC frame exceeds the supported length.");
            }

            int received = await transport.ReadAsync(input.AsMemory(inputCount), cancellationToken).ConfigureAwait(false);
            if (received == 0)
            {
                throw new EndOfStreamException("The HDLC byte transport closed the connection.");
            }

            inputCount += received;
        }
    }

    private HdlcFrame? TryDecodeBufferedFrame()
    {
        while (!resynchronizing)
        {
            var result = codec.Decode(new ReadOnlySequence<byte>(input.AsMemory(0, inputCount)),
                out bool checksumFailed, out long? verifiedFrameEnd);
            if (result.Status == DecodeStatus.NeedMoreData)
            {
                return null;
            }

            if (result.Status == DecodeStatus.Done)
            {
                return ConsumeFrame(result, 0);
            }

            if (!checksumFailed)
            {
                string prefix = Convert.ToHexString(input.AsSpan(0, Math.Min(inputCount, DiagnosticPrefixLength)));
                throw new InvalidDataException($"HDLC frame decode {result.Status}: {result.Error}; prefix={prefix}.");
            }

            linkObserver?.Invoke(new HdlcLinkEvent.ChecksumError(result.Error!));
            if (verifiedFrameEnd is long end)
            {
                // The verified header gives the frame length. Retain a possibly shared closing flag.
                DiscardInput(checked((int)end));
            }
            else
            {
                // The length is untrusted. Look for an independently validated frame after the opening flag(s).
                int firstData = input.AsSpan(0, inputCount).IndexOfAnyExcept(HdlcFrameFormat.Flag);
                DiscardInput(firstData);
                resynchronizing = true;
            }
        }

        return FindFrameBoundary();
    }

    private HdlcFrame? FindFrameBoundary()
    {
        int firstIncomplete = inputCount;
        for (int offset = 0; offset < inputCount; offset++)
        {
            if (input[offset] != HdlcFrameFormat.Flag
                || offset + 1 < inputCount && input[offset + 1] == HdlcFrameFormat.Flag)
            {
                continue;
            }

            var candidate = codec.Decode(new ReadOnlySequence<byte>(input.AsMemory(offset, inputCount - offset)));
            if (candidate.Status == DecodeStatus.Done)
            {
                resynchronizing = false;
                return ConsumeFrame(candidate, offset);
            }

            if (candidate.Status == DecodeStatus.NeedMoreData && firstIncomplete == inputCount)
            {
                firstIncomplete = offset;
            }
        }

        DiscardInput(firstIncomplete);
        return null;
    }

    private HdlcFrame ConsumeFrame(DecodeResult<HdlcFrame> result, int offset)
    {
        var frame = result.Value!;
        DiscardInput(checked(offset + (int)result.Consumed));
        frameObserver?.Invoke(frame, true);
        return frame;
    }

    private void DiscardInput(int count)
    {
        input.AsSpan(count, inputCount - count).CopyTo(input);
        inputCount -= count;
    }

    private async Task WriteFrameAsync(HdlcFrame frame, CancellationToken cancellationToken)
    {
        await writeLock.WaitAsync(cancellationToken);
        try
        {
            await WriteFrameCoreAsync(frame, cancellationToken);
        }
        finally
        {
            writeLock.Release();
        }
    }

    private async Task WriteFrameCoreAsync(HdlcFrame frame, CancellationToken cancellationToken)
    {
        byte[] bytes = codec.EncodeBytes(frame);
        frameObserver?.Invoke(frame, false);
        cancellationToken.ThrowIfCancellationRequested();
        await transport.WriteAsync(bytes, cancellationToken);
    }

    private void ValidateAddresses(HdlcFrame frame)
    {
        if (!DestinationMatches(frame) || !ServerMatches(frame.Source))
        {
            throw new InvalidDataException($"Unexpected HDLC frame addresses: expected client={local.Value}/{local.Length}, server={remote.Value}/{remote.Length}; received client={frame.Destination.Value}/{frame.Destination.Length}, server={frame.Source.Value}/{frame.Source.Length}.");
        }
    }

    private bool DestinationMatches(HdlcFrame frame) => frame.Destination == local
        || (frame.Destination == notificationAddress && IsDataNotificationFrame(frame));

    private static bool IsDataNotificationFrame(HdlcFrame frame) => frame.IsUnnumberedInformation
        && frame.Information.Length > LlcHeader.Length
        && frame.Information[LlcHeader.Length] == (byte)ApduTag.DataNotification;

    private bool ServerMatches(HdlcAddress source) => source == remote || remote.HasSameServerComponents(source);

    private void EnsureLinkAvailable()
    {
        EnsureRecoveryAvailable();
        if (linkFailure is not null)
        {
            throw new IOException(linkFailure);
        }
    }

    private void FailLink(string reason)
    {
        linkFailure = $"stage=HDLC data; {reason}; client={local.Value}/{local.Length}, server={remote.Value}/{remote.Length}; the HDLC link state is uncertain. Establish a new HDLC connection.";
        throw new IOException(linkFailure);
    }
}
