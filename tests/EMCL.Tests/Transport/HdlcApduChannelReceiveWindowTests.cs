using System.Collections.Concurrent;
using EMCL.Client.Association;
using EMCL.Client.Sessions;
using EMCL.Core.Codecs;
using EMCL.Core.Objects;
using EMCL.Core.Operations;
using EMCL.Core.Values;
using EMCL.Framing;
using EMCL.Framing.Hdlc.Frames;
using EMCL.Protocol.Acse;
using EMCL.Protocol.Apdu;

namespace EMCL.Tests.Transport;

public sealed partial class HdlcApduChannelTests
{
    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(3, 3)]
    [InlineData(4, 4)]
    [InlineData(5, 5)]
    [InlineData(6, 6)]
    [InlineData(7, 7)]
    [InlineData(7, 2)]
    [InlineData(4, 1)]
    public async Task ReceivesBurstsAndAcknowledgesOnlyFinalAcrossSequenceWrap(int window, int burstLength)
    {
        byte[] response = Enumerable.Range(0, 301).Select(index => (byte)index).ToArray();
        await RunLoopbackAsync(async (stream, codec, local, remote) =>
        {
            await WriteFrameAsync(stream, codec, new(local, remote, HdlcControl.Ua, ReceiveWindowUa(window)));
            Assert.True((await ReadFrameAsync(stream, codec)).IsInformation);
            var frames = WindowedResponse(remote, local, response);
            Assert.True(frames.Length > 16);
            for (int index = 0; index < frames.Length; index++)
            {
                bool final = (index + 1) % burstLength == 0 || index == frames.Length - 1;
                await WriteFrameAsync(stream, codec, SetFinal(frames[index], final));
                if (final && frames[index].Segmented)
                {
                    Assert.Equal(HdlcControl.CreateReceiveReady((byte)((index + 1) % 8)),
                        (await ReadFrameAsync(stream, codec)).Control);
                }
            }

            Assert.Equal(HdlcControl.Disc, (await ReadFrameAsync(stream, codec)).Control);
        }, async channel =>
        {
            Assert.Equal(window, Assert.IsType<HdlcApduChannel>(channel).Negotiation.Effective.ReceiveWindowFrames);
            await channel.SendAsync(new byte[] { 1 }, TestContext.Current.CancellationToken);
            Assert.Equal(response, (await channel.ReceiveAsync(TestContext.Current.CancellationToken))!.Value.ToArray());
        }, linkParameters: new(ReceiveWindowFrames: window), inspectSnrm: snrm =>
            Assert.Equal((byte)window, snrm.Information[^1]));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RequestsMissingMiddleSegmentWithoutLosingAcceptedPrefix(bool corrupt)
    {
        byte[] response = Enumerable.Range(0, 65).Select(index => (byte)index).ToArray();
        var diagnostics = new ConcurrentQueue<HdlcLinkEvent>();
        await RunLoopbackAsync(async (stream, codec, local, remote) =>
        {
            await WriteFrameAsync(stream, codec, new(local, remote, HdlcControl.Ua, ReceiveWindowUa(3)));
            Assert.True((await ReadFrameAsync(stream, codec)).IsInformation);
            var frames = WindowedResponse(remote, local, response);
            Assert.Equal(5, frames.Length);
            await WriteFrameAsync(stream, codec, frames[0]);
            if (corrupt)
            {
                byte[] damaged = codec.EncodeBytes(frames[1]);
                damaged[^2] ^= 1;
                await stream.WriteAsync(damaged, TestContext.Current.CancellationToken);
            }

            await WriteFrameAsync(stream, codec, SetFinal(frames[2]));
            Assert.Equal(HdlcControl.CreateReceiveReady(1), (await ReadFrameAsync(stream, codec)).Control);
            await WriteFrameAsync(stream, codec, frames[1]);
            await WriteFrameAsync(stream, codec, SetFinal(frames[2]));
            Assert.Equal(HdlcControl.CreateReceiveReady(3), (await ReadFrameAsync(stream, codec)).Control);
            await WriteFrameAsync(stream, codec, frames[3]);
            await WriteFrameAsync(stream, codec, SetFinal(frames[4]));
            Assert.Equal(HdlcControl.Disc, (await ReadFrameAsync(stream, codec)).Control);
        }, async channel =>
        {
            await channel.SendAsync(new byte[] { 1 }, TestContext.Current.CancellationToken);
            Assert.Equal(response, (await channel.ReceiveAsync(TestContext.Current.CancellationToken))!.Value.ToArray());
        }, linkParameters: new(ReceiveWindowFrames: 3), linkObserver: diagnostics.Enqueue);
        Assert.Contains(new HdlcLinkEvent.ReceiveSequenceError(1, 2), diagnostics);
        Assert.Equal(corrupt, diagnostics.OfType<HdlcLinkEvent.ChecksumError>().Any());
    }

    [Fact]
    public async Task SuppressesReplayOfEntireReceiveWindowAfterLostAcknowledgement()
    {
        byte[] response = Enumerable.Range(0, 65).Select(index => (byte)index).ToArray();
        await RunLoopbackAsync(async (stream, codec, local, remote) =>
        {
            await WriteFrameAsync(stream, codec, new(local, remote, HdlcControl.Ua, ReceiveWindowUa(3)));
            Assert.True((await ReadFrameAsync(stream, codec)).IsInformation);
            var frames = WindowedResponse(remote, local, response);
            for (int attempt = 0; attempt < 2; attempt++)
            {
                await WriteFrameAsync(stream, codec, frames[0]);
                await WriteFrameAsync(stream, codec, frames[1]);
                await WriteFrameAsync(stream, codec, SetFinal(frames[2]));
                Assert.Equal(HdlcControl.CreateReceiveReady(3), (await ReadFrameAsync(stream, codec)).Control);
            }

            await WriteFrameAsync(stream, codec, frames[3]);
            await WriteFrameAsync(stream, codec, SetFinal(frames[4]));
            Assert.Equal(HdlcControl.Disc, (await ReadFrameAsync(stream, codec)).Control);
        }, async channel =>
        {
            await channel.SendAsync(new byte[] { 1 }, TestContext.Current.CancellationToken);
            Assert.Equal(response, (await channel.ReceiveAsync(TestContext.Current.CancellationToken))!.Value.ToArray());
        }, linkParameters: new(ReceiveWindowFrames: 3));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReceiveTimerRecoversLostFinalOrContinuationPoll(bool lostPoll)
    {
        byte[] response = Enumerable.Range(0, 65).Select(index => (byte)index).ToArray();
        var diagnostics = new ConcurrentQueue<HdlcLinkEvent>();
        await RunLoopbackAsync(async (stream, codec, local, remote) =>
        {
            await WriteFrameAsync(stream, codec, new(local, remote, HdlcControl.Ua, ReceiveWindowUa(3)));
            Assert.True((await ReadFrameAsync(stream, codec)).IsInformation);
            var frames = WindowedResponse(remote, local, response);
            await WriteFrameAsync(stream, codec, frames[0]);
            int next = 1;
            if (lostPoll)
            {
                await WriteFrameAsync(stream, codec, frames[1]);
                await WriteFrameAsync(stream, codec, SetFinal(frames[2]));
                Assert.Equal(HdlcControl.CreateReceiveReady(3), (await ReadFrameAsync(stream, codec)).Control);
                next = 3;
            }

            Assert.Equal(HdlcControl.CreateReceiveReady((byte)next), (await ReadFrameAsync(stream, codec)).Control);
            for (int index = next; index < frames.Length; index++)
            {
                bool final = index == 2 || index == frames.Length - 1;
                await WriteFrameAsync(stream, codec, SetFinal(frames[index], final));
                if (final && frames[index].Segmented)
                {
                    Assert.Equal(HdlcControl.CreateReceiveReady(3), (await ReadFrameAsync(stream, codec)).Control);
                }
            }

            Assert.Equal(HdlcControl.Disc, (await ReadFrameAsync(stream, codec)).Control);
        }, async channel =>
        {
            await channel.SendAsync(new byte[] { 1 }, TestContext.Current.CancellationToken);
            Assert.Equal(response, (await channel.ReceiveAsync(TestContext.Current.CancellationToken))!.Value.ToArray());
        }, linkParameters: new(ReceiveWindowFrames: 3), acknowledgementTimeout: TimeSpan.FromMilliseconds(150),
            linkObserver: diagnostics.Enqueue);
        Assert.Contains(new HdlcLinkEvent.ReceiveRecoveryPoll(lostPoll ? (byte)3 : (byte)1, 1), diagnostics);
        Assert.Empty(diagnostics.OfType<HdlcLinkEvent.Retransmission>());
    }

    [Fact]
    public async Task ExhaustedReceiveRecoveryFaultsLinkAfterAcknowledgedRequest()
    {
        await RunLoopbackAsync(async (stream, codec, local, remote) =>
        {
            await WriteFrameAsync(stream, codec, new(local, remote, HdlcControl.Ua, ReceiveWindowUa(3)));
            Assert.True((await ReadFrameAsync(stream, codec)).IsInformation);
            await WriteFrameAsync(stream, codec, WindowedResponse(remote, local, new byte[40])[0]);
            Assert.Equal(HdlcControl.CreateReceiveReady(1), (await ReadFrameAsync(stream, codec)).Control);
            Assert.Equal(HdlcControl.Disc, (await ReadFrameAsync(stream, codec)).Control);
        }, async channel =>
        {
            await channel.SendAsync(new byte[] { 1 }, TestContext.Current.CancellationToken);
            var error = await Assert.ThrowsAsync<TimeoutException>(async () =>
                await channel.ReceiveAsync(TestContext.Current.CancellationToken));
            Assert.Contains("stage=HDLC receive", error.Message);
            await Assert.ThrowsAsync<TimeoutException>(async () =>
                await channel.SendAsync(new byte[] { 2 }, TestContext.Current.CancellationToken));
        }, linkParameters: new(ReceiveWindowFrames: 3), acknowledgementTimeout: TimeSpan.FromMilliseconds(150));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UiNotificationsPreserveReceiveWindowAndRespectFinal(bool uiFinal)
    {
        byte[] response = Enumerable.Range(0, 40).Select(index => (byte)index).ToArray();
        await RunLoopbackAsync(async (stream, codec, local, remote) =>
        {
            await WriteFrameAsync(stream, codec, new(local, remote, HdlcControl.Ua, ReceiveWindowUa(3)));
            Assert.True((await ReadFrameAsync(stream, codec)).IsInformation);
            var frames = WindowedResponse(remote, local, response);
            await WriteFrameAsync(stream, codec, frames[0]);
            await WriteFrameAsync(stream, codec, new(local, remote, uiFinal ? (byte)0x13 : (byte)0x03,
                [0xE6, 0xE7, 0, 0x0F]));
            if (uiFinal)
            {
                Assert.Equal(HdlcControl.CreateReceiveReady(1), (await ReadFrameAsync(stream, codec)).Control);
            }

            await WriteFrameAsync(stream, codec, frames[1]);
            await WriteFrameAsync(stream, codec, SetFinal(frames[2]));
            Assert.Equal(HdlcControl.Disc, (await ReadFrameAsync(stream, codec)).Control);
        }, async channel =>
        {
            await channel.SendAsync(new byte[] { 1 }, TestContext.Current.CancellationToken);
            Assert.Equal(new byte[] { 0x0F }, (await channel.ReceiveAsync(TestContext.Current.CancellationToken))!.Value.ToArray());
            Assert.Equal(response, (await channel.ReceiveAsync(TestContext.Current.CancellationToken))!.Value.ToArray());
        }, linkParameters: new(ReceiveWindowFrames: 3));
    }

    [Fact]
    public async Task RejectsPeerExceedingNegotiatedReceiveWindow()
    {
        await RunLoopbackAsync(async (stream, codec, local, remote) =>
        {
            await WriteFrameAsync(stream, codec, new(local, remote, HdlcControl.Ua, ReceiveWindowUa(2)));
            Assert.True((await ReadFrameAsync(stream, codec)).IsInformation);
            var frames = WindowedResponse(remote, local, new byte[40]);
            await WriteFrameAsync(stream, codec, frames[0]);
            await WriteFrameAsync(stream, codec, frames[1]);
            await WriteFrameAsync(stream, codec, SetFinal(frames[2]));
            Assert.Equal(HdlcControl.Disc, (await ReadFrameAsync(stream, codec)).Control);
        }, async channel =>
        {
            await channel.SendAsync(new byte[] { 1 }, TestContext.Current.CancellationToken);
            var error = await Assert.ThrowsAsync<IOException>(async () =>
                await channel.ReceiveAsync(TestContext.Current.CancellationToken));
            Assert.Contains("receive window", error.Message);
        }, linkParameters: new(ReceiveWindowFrames: 2));
    }

    [Fact]
    public async Task SendingWaitsForPeerFinalWhileUiNotificationIsDelivered()
    {
        var releasePeer = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        byte[] response = new byte[40];
        await RunLoopbackAsync(async (stream, codec, local, remote) =>
        {
            await WriteFrameAsync(stream, codec, new(local, remote, HdlcControl.Ua, ReceiveWindowUa(3)));
            Assert.True((await ReadFrameAsync(stream, codec)).IsInformation);
            var frames = WindowedResponse(remote, local, response);
            await WriteFrameAsync(stream, codec, frames[0]);
            await WriteFrameAsync(stream, codec, new(local, remote, 0x03, [0xE6, 0xE7, 0, 0x0F]));
            await releasePeer.Task.WaitAsync(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
            await WriteFrameAsync(stream, codec, frames[1]);
            await WriteFrameAsync(stream, codec, SetFinal(frames[2]));
            var next = await ReadFrameAsync(stream, codec);
            Assert.True(next.IsInformation);
            Assert.Equal((byte)1, next.SendSequence);
            Assert.Equal((byte)3, next.ReceiveSequence);
            Assert.Equal(HdlcControl.Disc, (await ReadFrameAsync(stream, codec)).Control);
        }, async channel =>
        {
            await channel.SendAsync(new byte[] { 1 }, TestContext.Current.CancellationToken);
            Assert.Equal(new byte[] { 0x0F }, (await channel.ReceiveAsync(TestContext.Current.CancellationToken))!.Value.ToArray());
            var sending = channel.SendAsync(new byte[] { 2 }, TestContext.Current.CancellationToken).AsTask();
            Assert.False(sending.IsCompleted);
            releasePeer.SetResult();
            Assert.Equal(response, (await channel.ReceiveAsync(TestContext.Current.CancellationToken))!.Value.ToArray());
            await sending;
        }, linkParameters: new(ReceiveWindowFrames: 3));
    }

    [Fact]
    public async Task CancelingWindowedReceiveFaultsLinkAndReleasesWaitingSenderWithoutRecovery()
    {
        await RunLoopbackAsync(async (stream, codec, local, remote) =>
        {
            await WriteFrameAsync(stream, codec, new(local, remote, HdlcControl.Ua, ReceiveWindowUa(3)));
            Assert.True((await ReadFrameAsync(stream, codec)).IsInformation);
            await WriteFrameAsync(stream, codec, WindowedResponse(remote, local, new byte[40])[0]);
            await WriteFrameAsync(stream, codec, new(local, remote, 0x03, [0xE6, 0xE7, 0, 0x0F]));
            Assert.Equal(HdlcControl.Disc, (await ReadFrameAsync(stream, codec)).Control);
        }, async channel =>
        {
            await channel.SendAsync(new byte[] { 1 }, TestContext.Current.CancellationToken);
            Assert.Equal(new byte[] { 0x0F }, (await channel.ReceiveAsync(TestContext.Current.CancellationToken))!.Value.ToArray());
            var sending = channel.SendAsync(new byte[] { 2 }, TestContext.Current.CancellationToken).AsTask();
            Assert.False(sending.IsCompleted);
            using var canceled = new CancellationTokenSource();
            await canceled.CancelAsync();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await channel.ReceiveAsync(canceled.Token));
            await Assert.ThrowsAsync<IOException>(() => sending);
        }, linkParameters: new(ReceiveWindowFrames: 3));
    }

    [Fact]
    public async Task TransmitWindowStaysOneEvenWithRecoveryDisabled()
    {
        await RunLoopbackAsync(async (stream, codec, local, remote) =>
        {
            await WriteFrameAsync(stream, codec, new(local, remote, HdlcControl.Ua, ReceiveWindowUa(3)));
            Assert.Equal((byte)0, (await ReadFrameAsync(stream, codec)).SendSequence);
            await WriteFrameAsync(stream, codec, new(local, remote, HdlcControl.CreateReceiveReady(1), []));
            await WriteFrameAsync(stream, codec, new(local, remote, 0x13, [0xE6, 0xE7, 0, 0x0F]));
            Assert.Equal((byte)1, (await ReadFrameAsync(stream, codec)).SendSequence);
            Assert.Equal(HdlcControl.Disc, (await ReadFrameAsync(stream, codec)).Control);
        }, async channel =>
        {
            await channel.SendAsync(new byte[] { 1 }, TestContext.Current.CancellationToken);
            var sending = channel.SendAsync(new byte[] { 2 }, TestContext.Current.CancellationToken).AsTask();
            Assert.False(sending.IsCompleted);
            Assert.Equal(new byte[] { 0x0F }, (await channel.ReceiveAsync(TestContext.Current.CancellationToken))!.Value.ToArray());
            await sending;
        }, linkParameters: new(ReceiveWindowFrames: 3));
    }

    [Fact]
    public async Task MultipleCompleteApdusAreReleasedAfterFinalSupervisoryFrame()
    {
        await RunLoopbackAsync(async (stream, codec, local, remote) =>
        {
            await WriteFrameAsync(stream, codec, new(local, remote, HdlcControl.Ua, ReceiveWindowUa(3)));
            Assert.True((await ReadFrameAsync(stream, codec)).IsInformation);
            await WriteFrameAsync(stream, codec, new(local, remote, 0x20, [0xE6, 0xE7, 0, 8]));
            await WriteFrameAsync(stream, codec, new(local, remote, 0x22, [0xE6, 0xE7, 0, 9]));
            await WriteFrameAsync(stream, codec, new(local, remote, HdlcControl.CreateReceiveReady(1), []));
            Assert.Equal(HdlcControl.Disc, (await ReadFrameAsync(stream, codec)).Control);
        }, async channel =>
        {
            await channel.SendAsync(new byte[] { 1 }, TestContext.Current.CancellationToken);
            Assert.Equal(new byte[] { 8 }, (await channel.ReceiveAsync(TestContext.Current.CancellationToken))!.Value.ToArray());
            Assert.Equal(new byte[] { 9 }, (await channel.ReceiveAsync(TestContext.Current.CancellationToken))!.Value.ToArray());
        }, linkParameters: new(ReceiveWindowFrames: 3));
    }

    [Fact]
    public async Task EachNonFinalFrameRestartsTimerAndFinalCompletionStopsIt()
    {
        var timeout = TimeSpan.FromMilliseconds(400);
        var diagnostics = new ConcurrentQueue<HdlcLinkEvent>();
        byte[] response = new byte[65];
        await RunLoopbackAsync(async (stream, codec, local, remote) =>
        {
            await WriteFrameAsync(stream, codec, new(local, remote, HdlcControl.Ua, ReceiveWindowUa(7)));
            Assert.True((await ReadFrameAsync(stream, codec)).IsInformation);
            var frames = WindowedResponse(remote, local, response);
            for (int index = 0; index < frames.Length; index++)
            {
                await WriteFrameAsync(stream, codec, SetFinal(frames[index], index == frames.Length - 1));
                if (index != frames.Length - 1)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(150), TestContext.Current.CancellationToken);
                }
            }

            // No receive poll is due after the complete final frame.
            Assert.True((await ReadFrameAsync(stream, codec)).IsInformation);
            Assert.Equal(HdlcControl.Disc, (await ReadFrameAsync(stream, codec)).Control);
        }, async channel =>
        {
            await channel.SendAsync(new byte[] { 1 }, TestContext.Current.CancellationToken);
            Assert.Equal(response, (await channel.ReceiveAsync(TestContext.Current.CancellationToken))!.Value.ToArray());
            await Task.Delay(timeout + timeout, TestContext.Current.CancellationToken);
            await channel.SendAsync(new byte[] { 2 }, TestContext.Current.CancellationToken);
        }, linkParameters: new(ReceiveWindowFrames: 7), acknowledgementTimeout: timeout,
            linkObserver: diagnostics.Enqueue);
        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task ParameterizedReplacementUaEnablesReceiveWindowBeforeFirstResponse()
    {
        var snapshots = new List<HdlcNegotiationResult>();
        byte[] response = new byte[40];
        await RunLoopbackAsync(async (stream, codec, local, remote) =>
        {
            await WriteFrameAsync(stream, codec, new(local, remote, HdlcControl.Ua, []));
            Assert.True((await ReadFrameAsync(stream, codec)).IsInformation);
            await WriteFrameAsync(stream, codec, new(local, remote, HdlcControl.Ua, ReceiveWindowUa(3)));
            var frames = WindowedResponse(remote, local, response);
            await WriteFrameAsync(stream, codec, frames[0]);
            await WriteFrameAsync(stream, codec, frames[1]);
            await WriteFrameAsync(stream, codec, SetFinal(frames[2]));
            Assert.Equal(HdlcControl.Disc, (await ReadFrameAsync(stream, codec)).Control);
        }, async channel =>
        {
            Assert.Equal(1, Assert.IsType<HdlcApduChannel>(channel).Negotiation.Effective.ReceiveWindowFrames);
            await channel.SendAsync(new byte[] { 1 }, TestContext.Current.CancellationToken);
            Assert.Equal(response, (await channel.ReceiveAsync(TestContext.Current.CancellationToken))!.Value.ToArray());
        }, linkParameters: new(ReceiveWindowFrames: 3), negotiationObserver: snapshots.Add);
        Assert.Equal(new[] { 1, 3 }, snapshots.Select(snapshot => snapshot.Effective.ReceiveWindowFrames));
    }

    [Fact]
    public async Task DlmsSessionAssociatesAndReadsLargeValueThroughReceiveWindows()
    {
        byte[] value = Enumerable.Range(0, 300).Select(index => (byte)index).ToArray();
        await RunLoopbackAsync(async (stream, codec, local, remote) =>
        {
            await WriteFrameAsync(stream, codec, new(local, remote, HdlcControl.Ua, ReceiveWindowUa(3)));
            Assert.True((await ReadFrameAsync(stream, codec)).IsInformation);
            var adapter = new HdlcApduAdapter(remote, local, response: true, maxInformationBytes: 16)
            {
                ReceiveSequence = 1,
                PollFinal = false
            };
            var apduCodec = new ApduCodec();
            await SendResponseAsync(AcseApdu.Aare(apduCodec.EncodeBytes(new InitiateResponse(null,
                DlmsNegotiation.Version6, (uint)DlmsConformance.Get, 1024))));
            var request = await ReadFrameAsync(stream, codec);
            Assert.Equal(adapter.NextSendSequence, request.ReceiveSequence);
            var get = Assert.IsType<LnRequest>(apduCodec.Decode(new(request.Information.AsMemory()[3..])).Value);
            adapter.ReceiveSequence = 2;
            await SendResponseAsync(new LnResponse(CosemOperationKind.Read, get.InvokeIdAndPriority,
                OperationResult.Ok(DlmsValue.Octets(value))));
            Assert.Equal(HdlcControl.Disc, (await ReadFrameAsync(stream, codec)).Control);

            async Task SendResponseAsync(DlmsApdu apdu)
            {
                var frames = adapter.Frame(apduCodec.EncodeBytes(apdu)).ToArray();
                for (int index = 0; index < frames.Length; index++)
                {
                    bool final = (index + 1) % 3 == 0 || index == frames.Length - 1;
                    await WriteFrameAsync(stream, codec, SetFinal(frames[index], final));
                    if (final && frames[index].Segmented)
                    {
                        Assert.Equal(HdlcControl.CreateReceiveReady((byte)((frames[index].SendSequence + 1) % 8)),
                            (await ReadFrameAsync(stream, codec)).Control);
                    }
                }
            }
        }, async channel =>
        {
            await using var client = new DlmsClientSession(channel, new ClientSessionOptions { LeaveOpen = true });
            Assert.Equal(DlmsConformance.Get, (await client.AssociateAsync(TestContext.Current.CancellationToken)).Conformance);
            Assert.Equal(value, (await client.GetAsync(1, LogicalName.Parse("0.0.96.1.0.255"), 2,
                cancellationToken: TestContext.Current.CancellationToken)).Bytes.ToArray());
        }, linkParameters: new(ReceiveWindowFrames: 3), acknowledgementTimeout: TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task SequenceGapEndedBySupervisoryFinalRequestsMissingSegment()
    {
        byte[] response = new byte[40];
        await RunLoopbackAsync(async (stream, codec, local, remote) =>
        {
            await WriteFrameAsync(stream, codec, new(local, remote, HdlcControl.Ua, ReceiveWindowUa(3)));
            Assert.True((await ReadFrameAsync(stream, codec)).IsInformation);
            var frames = WindowedResponse(remote, local, response);
            await WriteFrameAsync(stream, codec, frames[0]);
            await WriteFrameAsync(stream, codec, frames[2]);
            await WriteFrameAsync(stream, codec, new(local, remote, HdlcControl.CreateReceiveReady(1), []));
            Assert.Equal(HdlcControl.CreateReceiveReady(1), (await ReadFrameAsync(stream, codec)).Control);
            await WriteFrameAsync(stream, codec, frames[1]);
            await WriteFrameAsync(stream, codec, SetFinal(frames[2]));
            Assert.Equal(HdlcControl.Disc, (await ReadFrameAsync(stream, codec)).Control);
        }, async channel =>
        {
            await channel.SendAsync(new byte[] { 1 }, TestContext.Current.CancellationToken);
            Assert.Equal(response, (await channel.ReceiveAsync(TestContext.Current.CancellationToken))!.Value.ToArray());
        }, linkParameters: new(ReceiveWindowFrames: 3));
    }

    private static ImmutableArray<byte> ReceiveWindowUa(int peerTransmitWindow) =>
        [0x81, 0x80, 0x12, 0x05, 0x01, 0x80, 0x06, 0x01, 0x80,
            0x07, 0x04, 0, 0, 0, (byte)peerTransmitWindow, 0x08, 0x04, 0, 0, 0, 1];

    private static HdlcFrame[] WindowedResponse(HdlcAddress remote, HdlcAddress local, byte[] response) =>
        new HdlcApduAdapter(remote, local, response: true, maxInformationBytes: 16)
        {
            ReceiveSequence = 1,
            PollFinal = false
        }.Frame(response).ToArray();

    private static HdlcFrame SetFinal(HdlcFrame frame, bool final = true) => frame with
    {
        Control = (byte)((frame.Control & 0xEF) | (final ? 0x10 : 0))
    };
}
