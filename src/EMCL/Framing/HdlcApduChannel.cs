using System.Buffers;
using EMCL.Core.Codecs;
using EMCL.Framing.Hdlc.Frames;
using EMCL.Framing.Hdlc.Llc;
using EMCL.Protocol.Acse;
using EMCL.Transport.Channels;
using EMCL.Transport.Streams;

namespace EMCL.Framing;

/// <summary>Maintains a client HDLC link and exchanges complete APDUs over an application-supplied byte transport.</summary>
/// <remarks>Uses stop-and-wait sequencing, SNRM/UA setup, receive-ready acknowledgements, and DISC on disposal.
/// Outgoing APDUs must fit one negotiated information frame; one opt-in AARQ frame retransmission is supported.
/// General operation retries and outgoing segmentation are not supported.
/// A bounded number of SNRM/UA duplicates are tolerated before the first data response.
/// An initial empty UA may be followed by one UA with valid negotiation parameters.
/// A later UA makes the link state uncertain and requires a new connection.
/// Use one reader at a time and dispose after active operations complete.</remarks>
public sealed class HdlcApduChannel : IApduChannel, IAssociationRequestRetransmission
{
    private const int MaximumStaleFramesDuringLinkSetup = 32;
    private const int MaximumDuplicateUaFrames = 32;
    private const int DiagnosticPrefixLength = 16;
    private static readonly TimeSpan DisconnectTimeout = TimeSpan.FromSeconds(2);
    private readonly IByteTransport transport;
    private readonly HdlcCodec codec;
    private readonly HdlcAddress local;
    private readonly HdlcAddress remote;
    private readonly Action<HdlcFrame, bool>? frameObserver;
    private readonly SemaphoreSlim writeLock = new(1, 1);
    private readonly byte[] input = new byte[HdlcFrameFormat.MaximumFrameLength + HdlcFrameFormat.BoundaryFlagsLength];
    private HdlcApduAdapter? adapter;
    private HdlcFrame? setupUa;
    private HdlcFrame? firstDataFrame;
    private bool associationRetryUsed;
    private string? linkFailure;
    private bool awaitingFirstDataResponse = true;
    private int duplicateUaFrames;
    private int inputCount;
    private int disposed;
    private byte nextReceiveSequence;
    private byte nextSendSequence;

    private HdlcApduChannel(IByteTransport transport, HdlcAddress local, HdlcAddress remote,
        Action<HdlcFrame, bool>? frameObserver, bool allowMissingClosingFlag)
    {
        this.transport = transport;
        this.local = local;
        this.remote = remote;
        this.frameObserver = frameObserver;
        codec = new()
        {
            AllowMissingClosingFlag = allowMissingClosingFlag
        };
    }

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
        ConnectCoreAsync(transport, local, remote, timeout, limits, frameObserver,
            allowMissingClosingFlag: false, cancellationToken);

    /// <summary>Establishes an HDLC link with the supplied framing settings and takes ownership of the transport.</summary>
    /// <param name="transport">An already open byte transport.</param>
    /// <param name="framing">The addresses, setup timeout, and receive compatibility settings.</param>
    /// <param name="limits">Resource bounds for assembled APDUs.</param>
    /// <param name="cancellationToken">Cancels link setup.</param>
    public static Task<HdlcApduChannel> ConnectAsync(IByteTransport transport, ApduFraming.Hdlc framing,
        ProtocolLimits? limits = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(framing);
        return ConnectCoreAsync(transport, framing.Local, framing.Remote, framing.LinkTimeout, limits,
            framing.FrameObserver, framing.AllowMissingClosingFlag, cancellationToken);
    }

    private static async Task<HdlcApduChannel> ConnectCoreAsync(IByteTransport transport, HdlcAddress local,
        HdlcAddress remote, TimeSpan timeout, ProtocolLimits? limits, Action<HdlcFrame, bool>? frameObserver,
        bool allowMissingClosingFlag, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(transport);
        if (timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout));
        }

        var channel = new HdlcApduChannel(transport, local, remote, frameObserver, allowMissingClosingFlag);
        try
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(timeout);
            try
            {
                await channel.WriteFrameAsync(new(remote, local, HdlcControl.Snrm, []), deadline.Token);
                var response = await channel.WaitForUaAsync(deadline.Token);

                int maximumInformation = HdlcNegotiation.MaximumOutgoingInformation(response.Information);
                channel.setupUa = response;
                channel.adapter = new(local, remote, maxInformationBytes: maximumInformation, limits: limits);
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
        var dataAdapter = adapter ?? throw new InvalidOperationException("The HDLC link is not established.");
        while (true)
        {
            var frame = await ReadFrameAsync(cancellationToken).ConfigureAwait(false);
            ValidateAddresses(frame);
            if (frame.Control == HdlcControl.Ua)
            {
                await HandleDuplicateUaAsync(frame, cancellationToken).ConfigureAwait(false);
                continue;
            }

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

                continue;
            }

            if (frame.IsUnnumberedInformation)
            {
                return dataAdapter.Accept(frame with
                {
                    Source = remote
                });
            }

            ValidateSupervisoryFrame(frame);
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
        if (frame.SendSequence != nextReceiveSequence || frame.ReceiveSequence != nextSendSequence)
        {
            throw new InvalidDataException("Unexpected HDLC I-frame sequence or acknowledgement.");
        }

        nextReceiveSequence = (byte)((nextReceiveSequence + 1) & HdlcFrameFormat.SequenceMask);
        var apdu = dataAdapter.Accept(frame with
        {
            Source = remote
        });
        if (frame.Segmented)
        {
            await WriteFrameAsync(new(remote, local, HdlcControl.CreateReceiveReady(nextReceiveSequence), []),
                cancellationToken).ConfigureAwait(false);
        }

        return apdu;
    }

    private void ValidateSupervisoryFrame(HdlcFrame frame)
    {
        switch (frame.Control & HdlcControl.SupervisoryTypeMask)
        {
            case HdlcControl.ReceiveReady when frame.ReceiveSequence == nextSendSequence:
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
        await writeLock.WaitAsync(cancellationToken);
        try
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
            await WriteFrameCoreAsync(frames[0], cancellationToken);
            if (nextSendSequence == 1 && awaitingFirstDataResponse && firstDataFrame is null
                && !apdu.IsEmpty && apdu.Span[0] == (byte)AcseKind.Aarq)
            {
                firstDataFrame = frames[0];
            }
        }
        finally
        {
            writeLock.Release();
        }
    }

    /// <inheritdoc />
    public async ValueTask<bool> RetryUnansweredAssociationRequestAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        EnsureLinkAvailable();
        await writeLock.WaitAsync(cancellationToken);
        try
        {
            if (associationRetryUsed || !awaitingFirstDataResponse || nextReceiveSequence != 0
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
            if (frame.Destination != local || !ServerMatches(frame.Source))
            {
                // Some gateways flush frames from a previous client when a new connection opens.
                continue;
            }

            ValidateAddresses(frame);
            if (frame.Control == HdlcControl.Ua)
            {
                return frame;
            }

            if (frame.Control == HdlcControl.Dm)
            {
                throw new IOException($"stage=SNRM/UA; the meter rejected HDLC link establishment (DM); client={local.Value}/{local.Length}, server={remote.Value}/{remote.Length}.");
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

            int maximumInformation;
            try
            {
                maximumInformation = HdlcNegotiation.MaximumOutgoingInformation(frame.Information);
            }
            catch (InvalidDataException)
            {
                return false;
            }

            if (maximumInformation < LlcHeader.Length
                || firstDataFrame is not null && firstDataFrame.Information.Length > maximumInformation)
            {
                return false;
            }

            adapter!.SetMaximumInformationBytes(maximumInformation);
            setupUa = frame;
            duplicateUaFrames++;
            return true;
        }
        finally
        {
            writeLock.Release();
        }
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
        var result = codec.Decode(new ReadOnlySequence<byte>(input.AsMemory(0, inputCount)));
        if (result.Status == DecodeStatus.NeedMoreData)
        {
            return null;
        }

        if (result.Status != DecodeStatus.Done)
        {
            string prefix = Convert.ToHexString(input.AsSpan(0, Math.Min(inputCount, DiagnosticPrefixLength)));
            throw new InvalidDataException($"HDLC frame decode {result.Status}: {result.Error}; prefix={prefix}.");
        }

        var frame = result.Value!;
        int consumed = checked((int)result.Consumed);
        frameObserver?.Invoke(frame, true);
        input.AsSpan(consumed, inputCount - consumed).CopyTo(input);
        inputCount -= consumed;
        return frame;
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
        await transport.WriteAsync(bytes, cancellationToken);
    }

    private void ValidateAddresses(HdlcFrame frame)
    {
        if (frame.Destination != local || !ServerMatches(frame.Source))
        {
            throw new InvalidDataException($"Unexpected HDLC frame addresses: expected client={local.Value}/{local.Length}, server={remote.Value}/{remote.Length}; received client={frame.Destination.Value}/{frame.Destination.Length}, server={frame.Source.Value}/{frame.Source.Length}.");
        }
    }

    private bool ServerMatches(HdlcAddress source) => source == remote || remote.HasSameServerComponents(source);

    private void EnsureLinkAvailable()
    {
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
