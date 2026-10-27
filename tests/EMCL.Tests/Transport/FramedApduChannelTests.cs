using EMCL.Core.Codecs;
using EMCL.Framing;
using EMCL.Framing.Hdlc.Frames;
using EMCL.Framing.Wrapper;
using EMCL.Tests.Support;
using EMCL.Transport.Channels;
using EMCL.Transport.Streams;

namespace EMCL.Tests.Transport;

public sealed class FramedApduChannelTests
{
    [Fact]
    public async Task HandlesFragmentationSharedFlagsAndEndOfStream()
    {
        var apdu = FromHex("C001C100030100010800FF0200");
        var wrapper = new WrapperCodec().EncodeBytes(new(16, 1, apdu.ToImmutableArray()));
        await using (var c = new FramedApduChannel<WrapperFrame>(new ChunkTransport(wrapper.Concat(wrapper).ToArray(), 1), new WrapperCodec(), new WrapperApduAdapter(1, 16)))
        {
            await AssertReceivedBytesAsync(c, apdu);
            await AssertReceivedBytesAsync(c, apdu);
            Assert.True(await c.ReceiveAsync(cancellationToken: TestContext.Current.CancellationToken) == null);
        }

        await using (var c = new FramedApduChannel<WrapperFrame>(new StreamByteTransport(new MemoryStream(wrapper)), new WrapperCodec(), new WrapperApduAdapter(1, 16)))
        {
            await AssertReceivedBytesAsync(c, apdu);
        }

        await using (var c = new FramedApduChannel<WrapperFrame>(new ChunkTransport(wrapper[..^1], 2), new WrapperCodec(), new WrapperApduAdapter(1, 16)))
        {
            await Assert.ThrowsAsync<EndOfStreamException>(async () => await c.ReceiveAsync(cancellationToken: TestContext.Current.CancellationToken));
        }

        var sender = new HdlcApduAdapter(HdlcAddress.Client(16), HdlcAddress.Server(1), maxInformationBytes: 5);
        var codec = new HdlcCodec();
        var frames = sender.Frame(apdu).Select(codec.EncodeBytes).ToArray();
        var shared = frames[0].Concat(frames.Skip(1).SelectMany(f => f.Skip(1))).ToArray();
        await using (var c = new FramedApduChannel<HdlcFrame>(new ChunkTransport(shared, 1), codec, new HdlcApduAdapter(HdlcAddress.Server(1), HdlcAddress.Client(16))))
        {
            await AssertReceivedBytesAsync(c, apdu);
            Assert.True(await c.ReceiveAsync(cancellationToken: TestContext.Current.CancellationToken) == null);
        }

        var whole = new HdlcApduAdapter(HdlcAddress.Client(16), HdlcAddress.Server(1));
        var a = codec.EncodeBytes(whole.Frame(apdu).Single());
        var b = codec.EncodeBytes(whole.Frame(apdu).Single());
        await using (var c = new FramedApduChannel<HdlcFrame>(new ChunkTransport(a.Concat(b.Skip(1)).ToArray(), 1), codec, new HdlcApduAdapter(HdlcAddress.Server(1), HdlcAddress.Client(16))))
        {
            await AssertReceivedBytesAsync(c, apdu);
            await AssertReceivedBytesAsync(c, apdu);
            Assert.True(await c.ReceiveAsync(cancellationToken: TestContext.Current.CancellationToken) == null);
        }

        await using (var c = new FramedApduChannel<HdlcFrame>(new ChunkTransport(frames[0], 1), codec, new HdlcApduAdapter(HdlcAddress.Server(1), HdlcAddress.Client(16))))
        {
            await Assert.ThrowsAsync<EndOfStreamException>(async () => await c.ReceiveAsync(cancellationToken: TestContext.Current.CancellationToken));
        }

        using var output = new MemoryStream();
        await using (var c = new FramedApduChannel<WrapperFrame>(new StreamByteTransport(output, true), new WrapperCodec(), new WrapperApduAdapter(16, 1)))
        {
            await c.SendAsync(apdu, cancellationToken: TestContext.Current.CancellationToken);
        }

        AssertBytesEqual(wrapper, output.ToArray());
    }
}
