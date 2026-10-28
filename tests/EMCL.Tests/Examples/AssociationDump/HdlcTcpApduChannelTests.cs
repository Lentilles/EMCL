using System.Net;
using System.Net.Sockets;
using EMCL.Core.Codecs;
using EMCL.Examples.AssociationDump.Transport;
using EMCL.Framing;
using EMCL.Framing.Hdlc.Frames;
using EMCL.Transport.Streams;

namespace EMCL.Tests.Examples.AssociationDump;

public sealed class HdlcTcpApduChannelTests
{
    [Fact]
    public async Task EstablishesLinkAndAssemblesSegmentedResponseWithReceiveReadyAcknowledgements()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var local = HdlcAddress.Client(48);
        var remote = HdlcAddress.Server(1, 17);
        byte[] request = [1, 2, 3];
        byte[] response = [4, 5, 6, 7, 8, 9, 10, 11, 12, 13];
        using var trace = new StringWriter();
        var server = ServeAsync();
        using var tcp = new TcpClient();
        await tcp.ConnectAsync(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint).Port,
            TestContext.Current.CancellationToken);
        await using var channel = await HdlcTcpApduChannel.ConnectAsync(
            new StreamByteTransport(tcp.GetStream(), leaveOpen: true), local, remote, new(),
            TimeSpan.FromSeconds(5), trace, redactAarq: false, TestContext.Current.CancellationToken);

        await channel.SendAsync(request, TestContext.Current.CancellationToken);
        var received = await channel.ReceiveAsync(TestContext.Current.CancellationToken);
        Assert.True(received.HasValue);
        Assert.Equal(response, received.Value.ToArray());
        await channel.DisposeAsync();
        await server;
        Assert.Contains("[0001] TX SNRM", trace.ToString(), StringComparison.Ordinal);
        Assert.Contains("[0002] RX DM", trace.ToString(), StringComparison.Ordinal);
        Assert.Contains("[0003] RX I", trace.ToString(), StringComparison.Ordinal);
        Assert.Contains("[0004] RX UA", trace.ToString(), StringComparison.Ordinal);
        Assert.Contains("TX RR N(R)=1", trace.ToString(), StringComparison.Ordinal);
        Assert.Contains("TX RR N(R)=2", trace.ToString(), StringComparison.Ordinal);

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
        do
        {
            if (await stream.ReadAsync(one, timeout.Token) == 0)
            {
                throw new EndOfStreamException();
            }

            bytes.Add(one[0]);
        } while (one[0] != HdlcFrameFormat.Flag);

        var decoded = codec.Decode(new(bytes.ToArray()));
        Assert.Equal(DecodeStatus.Done, decoded.Status);
        return decoded.Value!;
    }
}
