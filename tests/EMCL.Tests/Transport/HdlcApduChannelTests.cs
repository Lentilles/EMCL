using System.Net;
using System.Net.Sockets;
using EMCL.Core.Codecs;
using EMCL.Client.Association;
using EMCL.Client.Sessions;
using EMCL.Framing;
using EMCL.Framing.Hdlc.Frames;
using EMCL.Protocol.Acse;
using EMCL.Protocol.Apdu;
using EMCL.Transport.Channels;
using EMCL.Transport.Streams;

namespace EMCL.Tests.Transport;

public sealed partial class HdlcApduChannelTests
{
    [Fact]
    public async Task CompatibilityReceivesUaAndInformationWithoutClosingFlags()
    {
        byte[] response = [4, 0x7E, 5];
        await RunLoopbackAsync(async (stream, codec, local, remote) =>
        {
            byte[] ua = codec.EncodeBytes(new(local, remote, HdlcControl.Ua, []));
            await stream.WriteAsync(ua.AsMemory(0, ua.Length - 1), TestContext.Current.CancellationToken);

            Assert.True((await ReadFrameAsync(stream, codec)).IsInformation);
            var adapter = new HdlcApduAdapter(remote, local, response: true) { ReceiveSequence = 1 };
            byte[] information = codec.EncodeBytes(adapter.Frame(response).Single());
            await stream.WriteAsync(information.AsMemory(0, information.Length - 1), TestContext.Current.CancellationToken);

            Assert.Equal(HdlcControl.Disc, (await ReadFrameAsync(stream, codec)).Control);
        }, async channel =>
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            deadline.CancelAfter(TimeSpan.FromSeconds(5));
            await channel.SendAsync(new byte[] { 1 }, deadline.Token);
            Assert.Equal(response, (await channel.ReceiveAsync(deadline.Token))!.Value.ToArray());
        }, allowMissingClosingFlag: true);
    }

    [Fact]
    public async Task PreservesPushBeforeUaAndBetweenSegmentsOfAare()
    {
        var token = TestContext.Current.CancellationToken;
        var notificationAddress = HdlcAddress.Client(0x40);
        await RunLoopbackAsync(async (stream, codec, local, remote) =>
        {
            await WriteFrameAsync(stream, codec, new(notificationAddress, remote, 0x13,
                [0xE6, 0xE7, 0, 0x0F, 0, 0, 0, 1, 0, 0]));
            await WriteFrameAsync(stream, codec, new(local, remote, HdlcControl.Ua, []));
            Assert.True((await ReadFrameAsync(stream, codec)).IsInformation);

            var apduCodec = new ApduCodec();
            var aare = AcseApdu.Aare(apduCodec.EncodeBytes(new InitiateResponse(null,
                DlmsNegotiation.Version6, (uint)DlmsConformance.Get, 1024)));
            var adapter = new HdlcApduAdapter(remote, local, response: true, maxInformationBytes: 32)
            {
                ReceiveSequence = 1
            };
            var segments = adapter.Frame(apduCodec.EncodeBytes(aare)).ToArray();
            Assert.Equal(2, segments.Length);
            await WriteFrameAsync(stream, codec, segments[0]);
            Assert.Equal(HdlcControl.CreateReceiveReady(1), (await ReadFrameAsync(stream, codec)).Control);
            await WriteFrameAsync(stream, codec, new(local, remote, 0x03,
                [0xE6, 0xE7, 0, 0x0F, 0, 0, 0, 2, 0, 0]));
            await WriteFrameAsync(stream, codec, segments[1]);
            Assert.Equal(HdlcControl.Disc, (await ReadFrameAsync(stream, codec)).Control);
        }, async channel =>
        {
            await using var client = new DlmsClientSession(channel, new ClientSessionOptions
            {
                RequestTimeout = TimeSpan.FromSeconds(3),
                LeaveOpen = true
            });
            Assert.Equal(DlmsConformance.Get, (await client.AssociateAsync(token)).Conformance);
            await using var notifications = client.ReadNotificationsAsync(token).GetAsyncEnumerator(token);
            Assert.True(await notifications.MoveNextAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5), token));
            Assert.Equal(1u, notifications.Current.LongInvokeIdAndPriority);
            Assert.True(await notifications.MoveNextAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5), token));
            Assert.Equal(2u, notifications.Current.LongInvokeIdAndPriority);
        }, notificationAddress: notificationAddress);
    }

    [Fact]
    public async Task UnansweredAarqIsRetransmittedWithSameHdlcSequenceAndAareIsPreserved()
    {
        await RunLoopbackAsync(async (stream, codec, local, remote) =>
        {
            await WriteFrameAsync(stream, codec, new(local, remote, HdlcControl.Ua, []));
            var first = await ReadFrameAsync(stream, codec);
            await WriteFrameAsync(stream, codec, new(local, remote, 0x13,
                [0xE6, 0xE7, 0, 0x0F, 0, 0, 0, 1, 0, 0]));
            var retry = await ReadFrameAsync(stream, codec);
            Assert.Equal(first.Control, retry.Control);
            Assert.Equal(first.Information, retry.Information);
            Assert.Equal((byte)0, retry.SendSequence);
            var response = AcseApdu.Aare(new ApduCodec().EncodeBytes(new InitiateResponse(null,
                DlmsNegotiation.Version6, (uint)new LnAssociationOptions().Conformance, 1024)));
            var adapter = new HdlcApduAdapter(remote, local, response: true) { ReceiveSequence = 1 };
            await WriteFrameAsync(stream, codec, adapter.Frame(new ApduCodec().EncodeBytes(response)).Single());
            Assert.Equal(HdlcControl.Disc, (await ReadFrameAsync(stream, codec)).Control);
        }, async channel =>
        {
            await using var client = new DlmsClientSession(channel, new ClientSessionOptions
            {
                AssociationRetryDelay = TimeSpan.FromMilliseconds(50),
                RequestTimeout = TimeSpan.FromSeconds(2),
                LeaveOpen = true
            });
            await client.AssociateAsync(TestContext.Current.CancellationToken);
        });
    }

    [Fact]
    public async Task AcknowledgedAarqIsNotRetransmittedWithoutAare()
    {
        await RunLoopbackAsync(async (stream, codec, local, remote) =>
        {
            await WriteFrameAsync(stream, codec, new(local, remote, HdlcControl.Ua, []));
            Assert.True((await ReadFrameAsync(stream, codec)).IsInformation);
            await WriteFrameAsync(stream, codec,
                new(local, remote, HdlcControl.CreateReceiveReady(1), []));
            Assert.Equal(HdlcControl.Disc, (await ReadFrameAsync(stream, codec)).Control);
        }, async channel =>
        {
            await using var client = new DlmsClientSession(channel, new ClientSessionOptions
            {
                AssociationRetryDelay = TimeSpan.FromMilliseconds(40),
                RequestTimeout = TimeSpan.FromMilliseconds(250),
                LeaveOpen = true
            });
            var error = await Assert.ThrowsAsync<TimeoutException>(async () =>
                await client.AssociateAsync(TestContext.Current.CancellationToken));
            Assert.Contains("stage=AARE", error.Message);
        });
    }

    [Fact]
    public async Task TwoOctetUaAndFourOctetDataFromSameServerPreserveApdu()
    {
        var extended = HdlcAddress.Server(1, 17, extended: true);
        var shortAddress = HdlcAddress.Server(1, 17);
        byte[] response = [0x61, 0x03, 0xA1];
        await RunLoopbackAsync(async (stream, codec, local, remote) =>
        {
            Assert.Equal(extended, remote);
            await WriteFrameAsync(stream, codec, new(local, shortAddress, HdlcControl.Ua, []));
            var request = await ReadFrameAsync(stream, codec);
            Assert.Equal(extended, request.Destination);
            var adapter = new HdlcApduAdapter(extended, local, response: true) { ReceiveSequence = 1 };
            await WriteFrameAsync(stream, codec, adapter.Frame(response).Single());
            Assert.Equal(HdlcControl.Disc, (await ReadFrameAsync(stream, codec)).Control);
        }, async channel =>
        {
            await channel.SendAsync(new byte[] { 1 }, TestContext.Current.CancellationToken);
            Assert.Equal(response, (await channel.ReceiveAsync(TestContext.Current.CancellationToken))!.Value.ToArray());
        }, configuredRemote: extended);
    }

    [Theory]
    [InlineData(2, 17)]
    [InlineData(1, 18)]
    public async Task DifferentServerComponentIsRejectedAfterLinkSetup(ushort logical, ushort physical)
    {
        var extended = HdlcAddress.Server(1, 17, extended: true);
        await RunLoopbackAsync(async (stream, codec, local, _) =>
        {
            await WriteFrameAsync(stream, codec, new(local, HdlcAddress.Server(1, 17), HdlcControl.Ua, []));
            Assert.True((await ReadFrameAsync(stream, codec)).IsInformation);
            await WriteFrameAsync(stream, codec, new(local, HdlcAddress.Server(logical, physical), HdlcControl.Ua, []));
            Assert.Equal(HdlcControl.Disc, (await ReadFrameAsync(stream, codec)).Control);
        }, async channel =>
        {
            await channel.SendAsync(new byte[] { 1 }, TestContext.Current.CancellationToken);
            var error = await Assert.ThrowsAsync<InvalidDataException>(async () =>
                await channel.ReceiveAsync(TestContext.Current.CancellationToken));
            Assert.Contains("addresses", error.Message);
        }, configuredRemote: extended);
    }

    [Fact]
    public async Task LateDuplicateUaBeforeAarqResponsePreservesApdu()
    {
        byte[] response = [4, 5, 6];
        var observed = new List<(byte Control, bool Received)>();
        await RunLoopbackAsync(async (stream, codec, local, remote) =>
        {
            await WriteFrameAsync(stream, codec, new(local, remote, HdlcControl.Ua, []));
            var request = await ReadFrameAsync(stream, codec);
            Assert.True(request.IsInformation);
            await WriteFrameAsync(stream, codec, new(local, remote, HdlcControl.Ua, []));
            var adapter = new HdlcApduAdapter(remote, local, response: true) { ReceiveSequence = 1 };
            await WriteFrameAsync(stream, codec, adapter.Frame(response).Single());
            Assert.Equal(HdlcControl.Disc, (await ReadFrameAsync(stream, codec)).Control);
        }, async channel =>
        {
            await channel.SendAsync(new byte[] { 1, 2, 3 }, TestContext.Current.CancellationToken);
            Assert.Equal(response, (await channel.ReceiveAsync(TestContext.Current.CancellationToken))!.Value.ToArray());
        }, (frame, received) => observed.Add((frame.Control, received)));

        Assert.Equal(6, observed.Count);
        Assert.Equal((HdlcControl.Snrm, false), observed[0]);
        Assert.Equal((HdlcControl.Ua, true), observed[1]);
        Assert.False(observed[2].Received);
        Assert.Equal((byte)0, (byte)(observed[2].Control & 1));
        Assert.Equal((HdlcControl.Ua, true), observed[3]);
        Assert.True(observed[4].Received);
        Assert.Equal((byte)0, (byte)(observed[4].Control & 1));
        Assert.Equal((HdlcControl.Disc, false), observed[5]);
    }

    [Theory]
    [InlineData("8180140502008006020080070400000001080400000001", false)]
    [InlineData("818003060140", true)]
    public async Task EmptyUaFollowedByParameterizedUaBeforeAarqPreservesApduAndNegotiation(
        string parametersHex, bool reducedFrameLimit)
    {
        byte[] response = [4, 5, 6];
        await RunLoopbackAsync(async (stream, codec, local, remote) =>
        {
            await WriteFrameAsync(stream, codec, new(local, remote, HdlcControl.Ua, []));
            await WriteFrameAsync(stream, codec, new(local, remote, HdlcControl.Ua,
                [.. Convert.FromHexString(parametersHex)]));
            Assert.True((await ReadFrameAsync(stream, codec)).IsInformation);
            var adapter = new HdlcApduAdapter(remote, local, response: true) { ReceiveSequence = 1 };
            await WriteFrameAsync(stream, codec, adapter.Frame(response).Single());
            Assert.Equal(HdlcControl.Disc, (await ReadFrameAsync(stream, codec)).Control);
        }, async channel =>
        {
            await channel.SendAsync(new byte[] { 1, 2, 3 }, TestContext.Current.CancellationToken);
            Assert.Equal(response, (await channel.ReceiveAsync(TestContext.Current.CancellationToken))!.Value.ToArray());
            if (reducedFrameLimit)
            {
                await Assert.ThrowsAsync<NotSupportedException>(async () =>
                    await channel.SendAsync(new byte[100], TestContext.Current.CancellationToken));
            }
        });
    }

    [Fact]
    public async Task UaWithWrongAddressesIsRejected()
    {
        await RunLoopbackAsync(async (stream, codec, local, remote) =>
        {
            await WriteFrameAsync(stream, codec, new(local, remote, HdlcControl.Ua, []));
            Assert.True((await ReadFrameAsync(stream, codec)).IsInformation);
            await WriteFrameAsync(stream, codec,
                new(HdlcAddress.Client(16), remote, HdlcControl.Ua, []));
            Assert.Equal(HdlcControl.Disc, (await ReadFrameAsync(stream, codec)).Control);
        }, async channel =>
        {
            await channel.SendAsync(new byte[] { 1 }, TestContext.Current.CancellationToken);
            var error = await Assert.ThrowsAsync<InvalidDataException>(async () =>
                await channel.ReceiveAsync(TestContext.Current.CancellationToken));
            Assert.Contains("addresses", error.Message);
        });
    }

    [Fact]
    public async Task UaWithTwentyThreeByteInformationBeforeAarqResponseRequiresNewLink()
    {
        await RunLoopbackAsync(async (stream, codec, local, remote) =>
        {
            await WriteFrameAsync(stream, codec, new(local, remote, HdlcControl.Ua, []));
            Assert.True((await ReadFrameAsync(stream, codec)).IsInformation);
            await WriteFrameAsync(stream, codec, new(local, remote, HdlcControl.Ua, new byte[23].ToImmutableArray()));
            Assert.Equal(HdlcControl.Disc, (await ReadFrameAsync(stream, codec)).Control);
        }, async channel =>
        {
            await channel.SendAsync(new byte[] { 1 }, TestContext.Current.CancellationToken);
            var error = await Assert.ThrowsAsync<IOException>(async () =>
                await channel.ReceiveAsync(TestContext.Current.CancellationToken));
            Assert.Contains("UA while awaiting the first data response after SNRM/UA", error.Message);
            Assert.Contains("stage=HDLC data", error.Message);
            Assert.Contains("client=", error.Message);
            Assert.Contains("server=", error.Message);
            Assert.Contains("Establish a new HDLC connection", error.Message);
            await Assert.ThrowsAsync<IOException>(async () =>
                await channel.SendAsync(new byte[] { 2 }, TestContext.Current.CancellationToken));
        });
    }

    [Theory]
    [InlineData(HdlcControl.Disc)]
    [InlineData(HdlcControl.Dm)]
    [InlineData(HdlcControl.Frmr)]
    [InlineData(HdlcControl.Snrm)]
    public async Task UnexpectedLinkControlRequiresNewLink(byte control)
    {
        await RunLoopbackAsync(async (stream, codec, local, remote) =>
        {
            await WriteFrameAsync(stream, codec, new(local, remote, HdlcControl.Ua, []));
            Assert.True((await ReadFrameAsync(stream, codec)).IsInformation);
            await WriteFrameAsync(stream, codec, new(local, remote, control, []));
            Assert.Equal(HdlcControl.Disc, (await ReadFrameAsync(stream, codec)).Control);
        }, async channel =>
        {
            await channel.SendAsync(new byte[] { 1 }, TestContext.Current.CancellationToken);
            var error = await Assert.ThrowsAsync<IOException>(async () =>
                await channel.ReceiveAsync(TestContext.Current.CancellationToken));
            Assert.Contains($"{control:X2}", error.Message);
            Assert.Contains("Establish a new HDLC connection", error.Message);
        });
    }

    [Fact]
    public async Task UaAfterDataResponseRequiresNewLink()
    {
        await RunLoopbackAsync(async (stream, codec, local, remote) =>
        {
            await WriteFrameAsync(stream, codec, new(local, remote, HdlcControl.Ua, []));
            Assert.True((await ReadFrameAsync(stream, codec)).IsInformation);
            var adapter = new HdlcApduAdapter(remote, local, response: true) { ReceiveSequence = 1 };
            await WriteFrameAsync(stream, codec, adapter.Frame(new byte[] { 8 }).Single());
            Assert.True((await ReadFrameAsync(stream, codec)).IsInformation);
            await WriteFrameAsync(stream, codec, new(local, remote, HdlcControl.Ua, []));
            Assert.Equal(HdlcControl.Disc, (await ReadFrameAsync(stream, codec)).Control);
        }, async channel =>
        {
            await channel.SendAsync(new byte[] { 1 }, TestContext.Current.CancellationToken);
            Assert.Equal([8], (await channel.ReceiveAsync(TestContext.Current.CancellationToken))!.Value.ToArray());
            await channel.SendAsync(new byte[] { 2 }, TestContext.Current.CancellationToken);
            var error = await Assert.ThrowsAsync<IOException>(async () =>
                await channel.ReceiveAsync(TestContext.Current.CancellationToken));
            Assert.Contains("UA after another frame was received during data exchange", error.Message);
        });
    }

    [Fact]
    public async Task EstablishesLinkAndAssemblesSegmentedResponseWithReceiveReadyAcknowledgements()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var local = HdlcAddress.Client(48);
        var remote = HdlcAddress.Server(1, 17);
        byte[] request = [1, 2, 3];
        byte[] response = [4, 5, 6, 7, 8, 9, 10, 11, 12, 13];
        var observed = new List<(HdlcFrame Frame, bool Received)>();
        var server = ServeAsync();
        using var tcp = new TcpClient();
        await tcp.ConnectAsync(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint).Port,
            TestContext.Current.CancellationToken);
        var factory = new SingleTransportFactory(new StreamByteTransport(tcp.GetStream(), leaveOpen: true));
        await using var channel = await ApduChannels.ConnectAsync(new(factory,
            new ApduFraming.Hdlc(local, remote, TimeSpan.FromSeconds(5),
                (frame, received) => observed.Add((frame, received))), new()),
            TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<NotSupportedException>(async () =>
            await channel.SendAsync(new byte[300], TestContext.Current.CancellationToken));
        await channel.SendAsync(request, TestContext.Current.CancellationToken);
        var received = await channel.ReceiveAsync(TestContext.Current.CancellationToken);
        Assert.True(received.HasValue);
        Assert.Equal(response, received.Value.ToArray());
        await channel.DisposeAsync();
        await server;
        Assert.Equal(1, factory.ConnectCount);
        Assert.Equal(HdlcControl.Snrm, observed[0].Frame.Control);
        Assert.False(observed[0].Received);
        Assert.Equal(HdlcControl.Dm, observed[1].Frame.Control);
        Assert.True(observed[1].Received);
        Assert.True(observed[2].Frame.IsInformation);
        Assert.Equal(HdlcControl.Ua, observed[3].Frame.Control);
        Assert.Contains(observed, item => !item.Received && item.Frame.Control == HdlcControl.CreateReceiveReady(1));
        Assert.Contains(observed, item => !item.Received && item.Frame.Control == HdlcControl.CreateReceiveReady(2));

        async Task ServeAsync()
        {
            using var peer = await listener.AcceptTcpClientAsync(TestContext.Current.CancellationToken);
            var stream = peer.GetStream();
            var codec = new HdlcCodec();
            var snrm = await ReadFrameAsync(stream, codec);
            Assert.Equal(HdlcControl.Snrm, snrm.Control);
            Assert.Equal(remote, snrm.Destination);
            Assert.Equal(local, snrm.Source);
            await stream.WriteAsync(codec.EncodeBytes(new(HdlcAddress.Client(16), remote, HdlcControl.Dm, [])),
                TestContext.Current.CancellationToken);
            var stale = new HdlcApduAdapter(remote, local, response: true).Frame(new byte[1]).Single();
            await stream.WriteAsync(codec.EncodeBytes(stale), TestContext.Current.CancellationToken);
            await stream.WriteAsync(codec.EncodeBytes(new(local, remote, HdlcControl.Ua, [])),
                TestContext.Current.CancellationToken);

            var information = await ReadFrameAsync(stream, codec);
            Assert.True(information.IsInformation);
            Assert.Equal((byte)0, information.SendSequence);
            var adapter = new HdlcApduAdapter(remote, local, response: true, maxInformationBytes: 6)
            {
                ReceiveSequence = 1
            };
            Assert.Equal(request, adapter.Accept(information)!.Value.ToArray());
            byte expectedReceiveSequence = 1;
            foreach (var segment in adapter.Frame(response))
            {
                await stream.WriteAsync(codec.EncodeBytes(segment), TestContext.Current.CancellationToken);
                if (!segment.Segmented)
                {
                    continue;
                }

                var acknowledgement = await ReadFrameAsync(stream, codec);
                Assert.Equal(HdlcControl.CreateReceiveReady(expectedReceiveSequence), acknowledgement.Control);
                expectedReceiveSequence++;
            }

            Assert.Equal(HdlcControl.Disc, (await ReadFrameAsync(stream, codec)).Control);
        }
    }

    [Fact]
    public async Task DiscUaDoesNotCarryOverToReconnectedTcpLink()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var local = HdlcAddress.Client(48);
        var remote = HdlcAddress.Server(1, 17);
        var codec = new HdlcCodec();
        var server = ServeAsync();
        var settings = new ApduChannelSettings(new TcpByteTransportFactory("127.0.0.1",
            ((IPEndPoint)listener.LocalEndpoint).Port, TimeSpan.FromSeconds(5)),
            new ApduFraming.Hdlc(local, remote, TimeSpan.FromSeconds(5)));

        await using (var first = await ApduChannels.ConnectAsync(settings, TestContext.Current.CancellationToken))
        {
            await first.DisposeAsync();
        }

        await using (var second = await ApduChannels.ConnectAsync(settings, TestContext.Current.CancellationToken))
        {
            await second.SendAsync(new byte[] { 1 }, TestContext.Current.CancellationToken);
            Assert.Equal([9], (await second.ReceiveAsync(TestContext.Current.CancellationToken))!.Value.ToArray());
        }

        await server;

        async Task ServeAsync()
        {
            using (var firstPeer = await listener.AcceptTcpClientAsync(TestContext.Current.CancellationToken))
            {
                var stream = firstPeer.GetStream();
                Assert.Equal(HdlcControl.Snrm, (await ReadFrameAsync(stream, codec)).Control);
                await WriteFrameAsync(stream, codec, new(local, remote, HdlcControl.Ua, []));
                Assert.Equal(HdlcControl.Disc, (await ReadFrameAsync(stream, codec)).Control);
                try
                {
                    await WriteFrameAsync(stream, codec, new(local, remote, HdlcControl.Ua, []));
                }
                catch (IOException)
                {
                    // The client may close immediately after DISC.
                }
            }

            using var secondPeer = await listener.AcceptTcpClientAsync(TestContext.Current.CancellationToken);
            var nextStream = secondPeer.GetStream();
            Assert.Equal(HdlcControl.Snrm, (await ReadFrameAsync(nextStream, codec)).Control);
            await WriteFrameAsync(nextStream, codec, new(local, remote, HdlcControl.Ua, []));
            Assert.True((await ReadFrameAsync(nextStream, codec)).IsInformation);
            var adapter = new HdlcApduAdapter(remote, local, response: true) { ReceiveSequence = 1 };
            await WriteFrameAsync(nextStream, codec, adapter.Frame(new byte[] { 9 }).Single());
            Assert.Equal(HdlcControl.Disc, (await ReadFrameAsync(nextStream, codec)).Control);
        }
    }

    private static async Task RunLoopbackAsync(
        Func<NetworkStream, HdlcCodec, HdlcAddress, HdlcAddress, Task> serve,
        Func<IApduChannel, Task> verify, Action<HdlcFrame, bool>? observer = null,
        HdlcAddress? configuredRemote = null, bool allowMissingClosingFlag = false,
        HdlcAddress? notificationAddress = null, TimeSpan? acknowledgementTimeout = null,
        Action<HdlcLinkEvent>? linkObserver = null, HdlcLinkParameters? linkParameters = null,
        Action<HdlcNegotiationResult>? negotiationObserver = null, Action<HdlcFrame>? inspectSnrm = null)
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var local = HdlcAddress.Client(48);
        var remote = configuredRemote ?? HdlcAddress.Server(1, 17);
        var server = ServeAsync();
        var settings = new ApduChannelSettings(new TcpByteTransportFactory("127.0.0.1",
            ((IPEndPoint)listener.LocalEndpoint).Port, TimeSpan.FromSeconds(5)),
            new ApduFraming.Hdlc(local, remote, TimeSpan.FromSeconds(5), observer)
            {
                AllowMissingClosingFlag = allowMissingClosingFlag,
                NotificationAddress = notificationAddress,
                InformationFrameRetryCount = acknowledgementTimeout.HasValue ? 1 : 0,
                AcknowledgementTimeout = acknowledgementTimeout ?? TimeSpan.FromSeconds(1),
                LinkObserver = linkObserver,
                LinkParameters = linkParameters,
                NegotiationObserver = negotiationObserver
            });
        try
        {
            await using var channel = await ApduChannels.ConnectAsync(settings, TestContext.Current.CancellationToken);
            await verify(channel);
        }
        finally
        {
            await server;
        }

        async Task ServeAsync()
        {
            using var peer = await listener.AcceptTcpClientAsync(TestContext.Current.CancellationToken);
            var stream = peer.GetStream();
            var codec = new HdlcCodec();
            var snrm = await ReadFrameAsync(stream, codec);
            Assert.Equal(HdlcControl.Snrm, snrm.Control);
            Assert.Equal(remote, snrm.Destination);
            Assert.Equal(local, snrm.Source);
            if (linkParameters is null)
            {
                Assert.Empty(snrm.Information);
            }

            inspectSnrm?.Invoke(snrm);
            await serve(stream, codec, local, remote);
        }
    }

    private static Task WriteFrameAsync(NetworkStream stream, HdlcCodec codec, HdlcFrame frame) =>
        stream.WriteAsync(codec.EncodeBytes(frame), TestContext.Current.CancellationToken).AsTask();

    private sealed class SingleTransportFactory(IByteTransport transport) : IByteTransportFactory
    {
        public int ConnectCount
        {
            get; private set;
        }

        public ValueTask<IByteTransport> ConnectAsync(CancellationToken cancellationToken = default)
        {
            ConnectCount++;
            return ValueTask.FromResult(transport);
        }
    }

    private static async Task<HdlcFrame> ReadFrameAsync(NetworkStream stream, HdlcCodec codec)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        byte[] one = new byte[1];
        var bytes = new List<byte>();
        do
        {
            if (await stream.ReadAsync(one, timeout.Token) == 0)
            {
                throw new EndOfStreamException();
            }
        } while (one[0] != HdlcFrameFormat.Flag);

        bytes.Add(one[0]);
        while (bytes.Count < 3)
        {
            if (await stream.ReadAsync(one, timeout.Token) == 0)
            {
                throw new EndOfStreamException();
            }

            bytes.Add(one[0]);
        }

        int frameLength = ((bytes[1] & HdlcFrameFormat.LengthHighMask) << 8) | bytes[2];
        while (bytes.Count < frameLength + HdlcFrameFormat.BoundaryFlagsLength)
        {
            if (await stream.ReadAsync(one, timeout.Token) == 0)
            {
                throw new EndOfStreamException();
            }

            bytes.Add(one[0]);
        }

        var decoded = codec.Decode(new(bytes.ToArray()));
        Assert.True(decoded.Status == DecodeStatus.Done,
            $"HDLC decode {decoded.Status}: {Convert.ToHexString(bytes.ToArray())}");
        return decoded.Value!;
    }
}
