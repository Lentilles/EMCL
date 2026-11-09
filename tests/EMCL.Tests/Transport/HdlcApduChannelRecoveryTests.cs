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
    [Fact]
    public async Task ReplaysLostAarqOnlyAfterRecoveryPollConfirmsItWasLost()
    {
        var token = TestContext.Current.CancellationToken;
        var diagnostics = new ConcurrentQueue<HdlcLinkEvent>();
        await RunLoopbackAsync(async (stream, codec, local, remote) =>
        {
            await WriteFrameAsync(stream, codec, new(local, remote, HdlcControl.Ua, []));
            var original = await ReadFrameAsync(stream, codec);
            Assert.True(original.IsInformation);
            Assert.Equal(HdlcControl.CreateReceiveReady(0), (await ReadFrameAsync(stream, codec)).Control);
            // A response without Final does not complete the recovery poll.
            await WriteFrameAsync(stream, codec, new(local, remote, HdlcControl.ReceiveReady, []));
            await WriteFrameAsync(stream, codec, new(local, remote, HdlcControl.CreateReceiveReady(0), []));
            var replay = await ReadFrameAsync(stream, codec);
            Assert.Equal(original.Control, replay.Control);
            Assert.Equal(original.Information, replay.Information);
            var responseCodec = new ApduCodec();
            var aare = AcseApdu.Aare(responseCodec.EncodeBytes(new InitiateResponse(null,
                DlmsNegotiation.Version6, (uint)DlmsConformance.Get, 1024)));
            var adapter = new HdlcApduAdapter(remote, local, response: true) { ReceiveSequence = 1 };
            await WriteFrameAsync(stream, codec, adapter.Frame(responseCodec.EncodeBytes(aare)).Single());
            Assert.Equal(HdlcControl.Disc, (await ReadFrameAsync(stream, codec)).Control);
        }, async channel =>
        {
            await using var client = new DlmsClientSession(channel, new ClientSessionOptions
            {
                AssociationRetryDelay = TimeSpan.FromMilliseconds(5),
                RequestTimeout = TimeSpan.FromSeconds(3),
                LeaveOpen = true
            });
            Assert.Equal(DlmsConformance.Get, (await client.AssociateAsync(token)).Conformance);
        }, acknowledgementTimeout: TimeSpan.FromMilliseconds(250), linkObserver: diagnostics.Enqueue);

        Assert.Equal(new HdlcLinkEvent[] { new HdlcLinkEvent.RecoveryPoll(0, 1), new HdlcLinkEvent.Retransmission(0, 1) },
            diagnostics.ToArray());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LostAcknowledgementDoesNotRepeatCommandAndDuplicateResultIsSuppressed(bool action)
    {
        var token = TestContext.Current.CancellationToken;
        var diagnostics = new ConcurrentQueue<HdlcLinkEvent>();
        var timeout = TimeSpan.FromMilliseconds(250);
        await RunLoopbackAsync(async (stream, codec, local, remote) =>
        {
            await WriteFrameAsync(stream, codec, new(local, remote, HdlcControl.Ua, []));
            Assert.True((await ReadFrameAsync(stream, codec)).IsInformation);
            var apduCodec = new ApduCodec();
            var adapter = new HdlcApduAdapter(remote, local, response: true) { ReceiveSequence = 1 };
            var aare = AcseApdu.Aare(apduCodec.EncodeBytes(new InitiateResponse(null,
                DlmsNegotiation.Version6, (uint)(DlmsConformance.Set | DlmsConformance.Action), 1024)));
            await WriteFrameAsync(stream, codec, adapter.Frame(apduCodec.EncodeBytes(aare)).Single());

            var command = await ReadFrameAsync(stream, codec);
            var request = Assert.IsType<LnRequest>(apduCodec.Decode(new(command.Information.AsMemory()[3..])).Value);
            var kind = action ? CosemOperationKind.Invoke : CosemOperationKind.Write;
            Assert.Equal(kind, request.Operation.Kind);

            // The simulated meter already accepted the command and only its acknowledgement was lost.
            Assert.Equal(HdlcControl.CreateReceiveReady(1), (await ReadFrameAsync(stream, codec)).Control);
            await WriteFrameAsync(stream, codec, new(local, remote, HdlcControl.CreateReceiveReady(2), []));
            adapter.ReceiveSequence = 2;
            var result = new LnResponse(kind, request.InvokeIdAndPriority, OperationResult.Ok());
            var response = adapter.Frame(apduCodec.EncodeBytes(result)).Single();
            byte[] damaged = codec.EncodeBytes(response);
            damaged[^2] ^= 1;
            await stream.WriteAsync(damaged, token);
            await Task.Delay(timeout + timeout, token);

            await WriteFrameAsync(stream, codec, response);
            await WriteFrameAsync(stream, codec, response);

            // A new APDU must be a release. Any extra poll or duplicate command appears here instead.
            var release = await ReadFrameAsync(stream, codec);
            Assert.Equal((byte)2, release.SendSequence);
            var releaseRequest = Assert.IsType<AcseApdu>(apduCodec.Decode(new(release.Information.AsMemory()[3..])).Value);
            Assert.Equal(AcseKind.Rlrq, releaseRequest.Kind);
            adapter.ReceiveSequence = 3;
            await WriteFrameAsync(stream, codec, adapter.Frame(apduCodec.EncodeBytes(AcseApdu.Release(response: true))).Single());
            Assert.Equal(HdlcControl.Disc, (await ReadFrameAsync(stream, codec)).Control);
        }, async channel =>
        {
            await using var client = new DlmsClientSession(channel, new ClientSessionOptions
            {
                RequestTimeout = TimeSpan.FromSeconds(3),
                LeaveOpen = true
            });
            await client.AssociateAsync(token);
            var logicalName = LogicalName.Parse("1.0.1.8.0.255");
            if (action)
            {
                Assert.Null(await client.ActionAsync(3, logicalName, 1, DlmsValue.Null, token));
            }
            else
            {
                await client.SetAsync(3, logicalName, 2, DlmsValue.Null, cancellationToken: token);
            }

            await client.ReleaseAsync(token);
        }, acknowledgementTimeout: timeout, linkObserver: diagnostics.Enqueue);

        Assert.Collection(diagnostics,
            diagnostic => Assert.Equal(new HdlcLinkEvent.RecoveryPoll(1, 1), diagnostic),
            diagnostic => Assert.Contains("FCS", Assert.IsType<HdlcLinkEvent.ChecksumError>(diagnostic).Error));
    }

    [Fact]
    public async Task DelayedResponseToPreviousCommandDoesNotAcknowledgeTheNextFrame()
    {
        var token = TestContext.Current.CancellationToken;
        await RunLoopbackAsync(async (stream, codec, local, remote) =>
        {
            await WriteFrameAsync(stream, codec, new(local, remote, HdlcControl.Ua, []));
            Assert.Equal((byte)0, (await ReadFrameAsync(stream, codec)).SendSequence);
            await WriteFrameAsync(stream, codec, new(local, remote, HdlcControl.CreateReceiveReady(1), []));
            var next = await ReadFrameAsync(stream, codec);
            Assert.Equal((byte)1, next.SendSequence);
            Assert.Equal((byte)0, next.ReceiveSequence);

            var adapter = new HdlcApduAdapter(remote, local, response: true) { ReceiveSequence = 1 };
            await WriteFrameAsync(stream, codec, adapter.Frame(new byte[] { 9 }).Single());
            Assert.Equal(HdlcControl.CreateReceiveReady(1), (await ReadFrameAsync(stream, codec)).Control);
            await WriteFrameAsync(stream, codec, new(local, remote, HdlcControl.CreateReceiveReady(1), []));
            var replay = await ReadFrameAsync(stream, codec);
            Assert.Equal(next.SendSequence, replay.SendSequence);
            Assert.Equal(next.Information, replay.Information);
            Assert.Equal((byte)1, replay.ReceiveSequence);

            adapter.ReceiveSequence = 2;
            await WriteFrameAsync(stream, codec, adapter.Frame(new byte[] { 10 }).Single());
            Assert.Equal(HdlcControl.Disc, (await ReadFrameAsync(stream, codec)).Control);
        }, async channel =>
        {
            await channel.SendAsync(new byte[] { 1 }, token);
            var sending = channel.SendAsync(new byte[] { 2 }, token).AsTask();
            Assert.Equal(new byte[] { 9 }, (await channel.ReceiveAsync(token))!.Value.ToArray());
            await sending;
            Assert.Equal(new byte[] { 10 }, (await channel.ReceiveAsync(token))!.Value.ToArray());
        }, acknowledgementTimeout: TimeSpan.FromMilliseconds(250));
    }

    [Theory]
    [InlineData("FCS", false)]
    [InlineData("FCS", true)]
    [InlineData("HCS", false)]
    [InlineData("HCS", true)]
    [InlineData("length", true)]
    public async Task DiscardsCorruptFrameAndPreservesNextFrameAcrossSharedFlagAndReads(string corrupt, bool notification)
    {
        var token = TestContext.Current.CancellationToken;
        var rejected = new TaskCompletionSource<HdlcLinkEvent>(TaskCreationOptions.RunContinuationsAsynchronously);
        await RunLoopbackAsync(async (stream, codec, local, remote) =>
        {
            await WriteFrameAsync(stream, codec, new(local, remote, HdlcControl.Ua, []));
            Assert.True((await ReadFrameAsync(stream, codec)).IsInformation);
            byte control = notification ? (byte)0x13 : (byte)0x30;
            byte[] damaged = codec.EncodeBytes(new(local, remote, control, [0xe6, 0xe7, 0, 0x7e, 0xa7, 0xff, 0x02]));
            int index = corrupt switch
            {
                "HCS" => 1 + HdlcFrameFormat.FormatFieldLength + local.Length + remote.Length + 1,
                "FCS" => damaged.Length - 2,
                _ => 2
            };
            damaged[index] ^= corrupt == "length" ? (byte)0x80 : (byte)0x01;
            byte[] response = codec.EncodeBytes(new(local, remote, 0x30, [0xe6, 0xe7, 0, 9]));
            await stream.WriteAsync(damaged, token);
            var diagnostic = Assert.IsType<HdlcLinkEvent.ChecksumError>(
                await rejected.Task.WaitAsync(TimeSpan.FromSeconds(3), token));
            Assert.Contains(corrupt == "FCS" ? "FCS" : "HCS", diagnostic.Error);
            await stream.WriteAsync(response.AsMemory(1), token);
            Assert.Equal(HdlcControl.Disc, (await ReadFrameAsync(stream, codec)).Control);
        }, async channel =>
        {
            await channel.SendAsync(new byte[] { 1 }, token);
            Assert.Equal(new byte[] { 9 }, (await channel.ReceiveAsync(token))!.Value.ToArray());
        }, acknowledgementTimeout: TimeSpan.FromSeconds(1), linkObserver: diagnostic => rejected.TrySetResult(diagnostic));
    }

    [Fact]
    public async Task ExhaustedRecoveryFaultsLinkWithoutReplayingAnUnconfirmedCommand()
    {
        var token = TestContext.Current.CancellationToken;
        await RunLoopbackAsync(async (stream, codec, local, remote) =>
        {
            await WriteFrameAsync(stream, codec, new(local, remote, HdlcControl.Ua, []));
            Assert.True((await ReadFrameAsync(stream, codec)).IsInformation);
            Assert.Equal(HdlcControl.CreateReceiveReady(0), (await ReadFrameAsync(stream, codec)).Control);
            Assert.Equal(HdlcControl.Disc, (await ReadFrameAsync(stream, codec)).Control);
        }, async channel =>
        {
            await channel.SendAsync(new byte[] { 1 }, token);
            var error = await Assert.ThrowsAsync<TimeoutException>(async () => await channel.ReceiveAsync(token));
            Assert.Contains("HDLC acknowledgement", error.Message);
            await Assert.ThrowsAsync<TimeoutException>(async () => await channel.SendAsync(new byte[] { 2 }, token));
        }, acknowledgementTimeout: TimeSpan.FromMilliseconds(100));
    }

    [Fact]
    public async Task CancellationPreventsReplayEvenIfThePeerRequestsIt()
    {
        var token = TestContext.Current.CancellationToken;
        using var exchange = CancellationTokenSource.CreateLinkedTokenSource(token);
        await RunLoopbackAsync(async (stream, codec, local, remote) =>
        {
            await WriteFrameAsync(stream, codec, new(local, remote, HdlcControl.Ua, []));
            Assert.True((await ReadFrameAsync(stream, codec)).IsInformation);
            Assert.Equal(HdlcControl.CreateReceiveReady(0), (await ReadFrameAsync(stream, codec)).Control);
            await exchange.CancelAsync();
            await WriteFrameAsync(stream, codec, new(local, remote, HdlcControl.CreateReceiveReady(0), []));
            Assert.Equal(HdlcControl.Disc, (await ReadFrameAsync(stream, codec)).Control);
        }, async channel =>
        {
            await channel.SendAsync(new byte[] { 1 }, exchange.Token);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await channel.ReceiveAsync(token));
        }, acknowledgementTimeout: TimeSpan.FromMilliseconds(250));
    }
}
