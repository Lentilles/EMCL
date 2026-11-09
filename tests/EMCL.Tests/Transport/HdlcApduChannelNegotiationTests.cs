using EMCL.Core.Codecs;
using EMCL.Framing;
using EMCL.Framing.Hdlc.Frames;
using EMCL.Tests.Support;

namespace EMCL.Tests.Transport;

public sealed partial class HdlcApduChannelTests
{
    [Theory]
    [InlineData(571, 1024, 256, 512, 512, 256)]
    [InlineData(1024, 256, 571, 1024, 1024, 256)]
    [InlineData(2030, 2030, 2030, 2030, 2030, 2030)]
    public async Task ExchangesExactMaximumFramesWithAsymmetricLimits(int localTx, int localRx,
        int peerTx, int peerRx, int effectiveTx, int effectiveRx)
    {
        var proposed = new HdlcLinkParameters(localTx, localRx);
        var peerParameters = new HdlcLinkParameters(peerTx, peerRx);
        var snapshots = new List<HdlcNegotiationResult>();
        byte[] request = Enumerable.Repeat((byte)0xA5, effectiveTx - 3).ToArray();
        byte[] response = Enumerable.Repeat((byte)0x5A, effectiveRx - 3).ToArray();
        await RunLoopbackAsync(async (stream, codec, local, remote) =>
        {
            await WriteFrameAsync(stream, codec, new(local, remote, HdlcControl.Ua,
                HdlcNegotiation.CreateSnrmInformation(peerParameters)));
            var received = await ReadFrameAsync(stream, codec);
            Assert.False(received.Segmented);
            Assert.Equal(effectiveTx, received.Information.Length);
            Assert.Equal(new byte[] { 0xE6, 0xE6, 0 }.Concat(request), received.Information);
            var adapter = new HdlcApduAdapter(remote, local, response: true, maxInformationBytes: effectiveRx)
            {
                ReceiveSequence = 1
            };
            await WriteFrameAsync(stream, codec, adapter.Frame(response).Single());
            Assert.Equal(HdlcControl.Disc, (await ReadFrameAsync(stream, codec)).Control);
        }, async channel =>
        {
            var hdlc = Assert.IsType<HdlcApduChannel>(channel);
            Assert.Equal(proposed, hdlc.Negotiation.Proposed);
            Assert.Equal(new(peerTx, peerRx, 1, 1), hdlc.Negotiation.Received);
            Assert.Equal(new(effectiveTx, effectiveRx), hdlc.Negotiation.Effective);
            await Assert.ThrowsAsync<NotSupportedException>(async () =>
                await channel.SendAsync(new byte[effectiveTx - 2], TestContext.Current.CancellationToken));
            await channel.SendAsync(request, TestContext.Current.CancellationToken);
            Assert.Equal(response, (await channel.ReceiveAsync(TestContext.Current.CancellationToken))!.Value.ToArray());
        }, linkParameters: proposed, negotiationObserver: snapshots.Add, inspectSnrm: snrm =>
            Assert.Equal(HdlcNegotiation.CreateSnrmInformation(proposed), snrm.Information));
        Assert.Single(snapshots);
    }

    [Fact]
    public async Task EmptyUaKeepsDefaultFrameSizeDespiteLargeProposal()
    {
        await RunLoopbackAsync(async (stream, codec, local, remote) =>
        {
            await WriteFrameAsync(stream, codec, new(local, remote, HdlcControl.Ua, []));
            Assert.Equal(128, (await ReadFrameAsync(stream, codec)).Information.Length);
            Assert.Equal(HdlcControl.Disc, (await ReadFrameAsync(stream, codec)).Control);
        }, async channel =>
        {
            await Assert.ThrowsAsync<NotSupportedException>(async () =>
                await channel.SendAsync(new byte[126], TestContext.Current.CancellationToken));
            await channel.SendAsync(new byte[125], TestContext.Current.CancellationToken);
        }, linkParameters: new(1024, 1024));
    }

    [Fact]
    public async Task LargeIncomingSegmentsStillRequireOneFrameAcknowledgements()
    {
        byte[] response = Enumerable.Repeat((byte)0xA5, 1000).ToArray();
        await RunLoopbackAsync(async (stream, codec, local, remote) =>
        {
            await WriteFrameAsync(stream, codec, new(local, remote, HdlcControl.Ua,
                [.. Convert.FromHexString("8180140502023B0602023B070400000001080400000001")]));
            Assert.True((await ReadFrameAsync(stream, codec)).IsInformation);
            var adapter = new HdlcApduAdapter(remote, local, response: true, maxInformationBytes: 571)
            {
                ReceiveSequence = 1
            };
            var frames = adapter.Frame(response).ToArray();
            Assert.Equal(2, frames.Length);
            Assert.Equal(571, frames[0].Information.Length);
            await WriteFrameAsync(stream, codec, frames[0]);
            Assert.Equal(HdlcControl.CreateReceiveReady(1), (await ReadFrameAsync(stream, codec)).Control);
            await WriteFrameAsync(stream, codec, frames[1]);
            Assert.Equal(HdlcControl.Disc, (await ReadFrameAsync(stream, codec)).Control);
        }, async channel =>
        {
            await channel.SendAsync(new byte[] { 1 }, TestContext.Current.CancellationToken);
            Assert.Equal(response, (await channel.ReceiveAsync(TestContext.Current.CancellationToken))!.Value.ToArray());
        }, linkParameters: new(571, 571), acknowledgementTimeout: TimeSpan.FromSeconds(3));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RejectsOversizedNumberedAndUiInformation(bool ui)
    {
        await RunLoopbackAsync(async (stream, codec, local, remote) =>
        {
            await WriteFrameAsync(stream, codec, new(local, remote, HdlcControl.Ua,
                [.. Convert.FromHexString("818003050120")]));
            Assert.True((await ReadFrameAsync(stream, codec)).IsInformation);
            await WriteFrameAsync(stream, codec, new(local, remote, ui ? (byte)0x13 : (byte)0x30,
                [0xE6, 0xE7, 0, .. new byte[30]]));
            Assert.Equal(HdlcControl.Disc, (await ReadFrameAsync(stream, codec)).Control);
        }, async channel =>
        {
            await channel.SendAsync(new byte[] { 1 }, TestContext.Current.CancellationToken);
            var error = await Assert.ThrowsAsync<IOException>(async () =>
                await channel.ReceiveAsync(TestContext.Current.CancellationToken));
            Assert.Contains("33 bytes", error.Message);
            Assert.Contains("32 bytes", error.Message);
            await Assert.ThrowsAsync<IOException>(async () =>
                await channel.SendAsync(new byte[] { 2 }, TestContext.Current.CancellationToken));
        });
    }

    [Fact]
    public async Task ReplacementUaUpdatesBothLimitsAndDiagnosticsBeforeFirstResponse()
    {
        var snapshots = new List<HdlcNegotiationResult>();
        await RunLoopbackAsync(async (stream, codec, local, remote) =>
        {
            await WriteFrameAsync(stream, codec, new(local, remote, HdlcControl.Ua, []));
            Assert.True((await ReadFrameAsync(stream, codec)).IsInformation);
            var ua = new HdlcFrame(local, remote, HdlcControl.Ua,
                [.. Convert.FromHexString("8180140502023B06020100070400000001080400000001")]);
            await WriteFrameAsync(stream, codec, ua);
            await WriteFrameAsync(stream, codec, ua);
            var adapter = new HdlcApduAdapter(remote, local, response: true, maxInformationBytes: 571)
            {
                ReceiveSequence = 1
            };
            await WriteFrameAsync(stream, codec, adapter.Frame(new byte[568]).Single());
            Assert.Equal(256, (await ReadFrameAsync(stream, codec)).Information.Length);
            Assert.Equal(HdlcControl.Disc, (await ReadFrameAsync(stream, codec)).Control);
        }, async channel =>
        {
            Assert.Equal(new(), Assert.IsType<HdlcApduChannel>(channel).Negotiation.Effective);
            await channel.SendAsync(new byte[] { 1 }, TestContext.Current.CancellationToken);
            Assert.Equal(568, (await channel.ReceiveAsync(TestContext.Current.CancellationToken))!.Value.Length);
            await channel.SendAsync(new byte[253], TestContext.Current.CancellationToken);
            Assert.Equal(new(256, 571), Assert.IsType<HdlcApduChannel>(channel).Negotiation.Effective);
        }, linkParameters: new(1024, 1024), negotiationObserver: snapshots.Add);
        Assert.Equal(2, snapshots.Count);
        Assert.Null(snapshots[0].Received);
        Assert.Equal(new(571, 256, 1, 1), snapshots[1].Received);
    }

    [Fact]
    public async Task ReplacementUaCannotInvalidateAlreadySentNonAarqFrame()
    {
        await RunLoopbackAsync(async (stream, codec, local, remote) =>
        {
            await WriteFrameAsync(stream, codec, new(local, remote, HdlcControl.Ua, []));
            Assert.Equal(103, (await ReadFrameAsync(stream, codec)).Information.Length);
            await WriteFrameAsync(stream, codec, new(local, remote, HdlcControl.Ua,
                [.. Convert.FromHexString("818003060140")]));
            Assert.Equal(HdlcControl.Disc, (await ReadFrameAsync(stream, codec)).Control);
        }, async channel =>
        {
            await channel.SendAsync(new byte[100], TestContext.Current.CancellationToken);
            await Assert.ThrowsAsync<IOException>(async () =>
                await channel.ReceiveAsync(TestContext.Current.CancellationToken));
            Assert.Null(Assert.IsType<HdlcApduChannel>(channel).Negotiation.Received);
        });
    }

    [Theory]
    [InlineData("818006070400000002")]
    [InlineData("818006080400000002")]
    [InlineData("818003060101")]
    public async Task InvalidUaFailsSetup(string hex)
    {
        var local = HdlcAddress.Client(16);
        var remote = HdlcAddress.Server(1);
        var bytes = new HdlcCodec().EncodeBytes(new(local, remote, HdlcControl.Ua,
            [.. Convert.FromHexString(hex)]));
        await Assert.ThrowsAsync<InvalidDataException>(() => HdlcApduChannel.ConnectAsync(
            new ChunkTransport(bytes, chunk: 7), new ApduFraming.Hdlc(local, remote, TimeSpan.FromSeconds(1)),
            cancellationToken: TestContext.Current.CancellationToken));
    }
}
