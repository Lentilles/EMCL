using EMCL.Core.Codecs;
using EMCL.Framing;
using EMCL.Framing.Hdlc.Frames;
using EMCL.Framing.Wrapper;
using EMCL.Transport.Streams;

namespace EMCL.Tests.Transport;

public sealed class ApduChannelsTests
{
    [Fact]
    public async Task OpensCustomTransportOnceAndDisposesItWithChannel()
    {
        var transport = new RecordingTransport();
        var factory = new RecordingFactory(transport);
        var settings = new ApduChannelSettings(factory, new ApduFraming.Wrapper(16, 1));

        await using (var channel = await ApduChannels.ConnectAsync(settings, TestContext.Current.CancellationToken))
        {
            await channel.SendAsync(new byte[] { 1, 2, 3 }, TestContext.Current.CancellationToken);
            Assert.Equal(1, factory.ConnectCount);
            var frame = new WrapperCodec().Decode(new(transport.Written.ToArray()));
            Assert.Equal(DecodeStatus.Done, frame.Status);
            Assert.Equal((ushort)16, frame.Value!.SourcePort);
            Assert.Equal((ushort)1, frame.Value.DestinationPort);
            Assert.Equal([1, 2, 3], frame.Value.Payload.ToArray());
        }

        Assert.Equal(1, transport.DisposeCount);
    }

    [Fact]
    public async Task DisposesOpenedTransportWhenFramingCreationFails()
    {
        var transport = new RecordingTransport();
        var factory = new RecordingFactory(transport);
        var settings = new ApduChannelSettings(factory, new ApduFraming.Wrapper(16, 1),
            new ProtocolLimits { MaxMessageBytes = 0 });

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
            await ApduChannels.ConnectAsync(settings, TestContext.Current.CancellationToken));
        Assert.Equal(1, factory.ConnectCount);
        Assert.Equal(1, transport.DisposeCount);
    }

    [Fact]
    public async Task InvalidHdlcSettingsDoNotOpenTransport()
    {
        var factory = new RecordingFactory(new RecordingTransport());
        var settings = new ApduChannelSettings(factory, new ApduFraming.Hdlc(
            HdlcAddress.Client(16), HdlcAddress.Server(1), TimeSpan.Zero));

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
            await ApduChannels.ConnectAsync(settings, TestContext.Current.CancellationToken));
        Assert.Equal(0, factory.ConnectCount);
    }

    private sealed class RecordingFactory(IByteTransport transport) : IByteTransportFactory
    {
        public int ConnectCount { get; private set; }

        public ValueTask<IByteTransport> ConnectAsync(CancellationToken cancellationToken = default)
        {
            ConnectCount++;
            return ValueTask.FromResult(transport);
        }
    }

    private sealed class RecordingTransport : IByteTransport
    {
        public List<byte> Written { get; } = [];
        public int DisposeCount { get; private set; }

        public ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(0);

        public ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            Written.AddRange(buffer.ToArray());
            return ValueTask.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            DisposeCount++;
            return ValueTask.CompletedTask;
        }
    }
}
