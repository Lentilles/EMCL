using EMCL.Core.Codecs;
using EMCL.Core.Values;
using EMCL.Framing;
using EMCL.Framing.Hdlc.Frames;
using EMCL.Framing.Wrapper;
using EMCL.Protocol.Apdu;
using EMCL.Tests.Support;
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
    public async Task WrapperAcceptsNotificationsOnConfiguredPortAndRejectsOtherTrafficThere()
    {
        byte[] notification = new ApduCodec().EncodeBytes(new DataNotification(1, [], DlmsValue.Null));
        var codec = new WrapperCodec();
        byte[] frames =
        [
            .. codec.EncodeBytes(new(1, 64, notification.ToImmutableArray())),
            .. codec.EncodeBytes(new(1, 64, [0x61, 0x00]))
        ];
        var factory = new RecordingFactory(new ChunkTransport(frames, chunk: 3));
        var settings = new ApduChannelSettings(factory, new ApduFraming.Wrapper(16, 1) { NotificationPort = 64 });
        var token = TestContext.Current.CancellationToken;

        await using var channel = await ApduChannels.ConnectAsync(settings, token);
        Assert.Equal(notification, (await channel.ReceiveAsync(token))!.Value.ToArray());
        await Assert.ThrowsAsync<InvalidDataException>(async () => await channel.ReceiveAsync(token));
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

    [Theory]
    [InlineData(2, 128, 1, 1)]
    [InlineData(128, 2031, 1, 1)]
    [InlineData(128, 128, 2, 1)]
    [InlineData(128, 128, 1, 0)]
    [InlineData(128, 128, 1, 8)]
    public async Task InvalidHdlcProposalDoesNotOpenTransport(int transmit, int receive, int windowTx, int windowRx)
    {
        var factory = new RecordingFactory(new RecordingTransport());
        var framing = new ApduFraming.Hdlc(HdlcAddress.Client(16), HdlcAddress.Server(1), TimeSpan.FromSeconds(1))
        {
            LinkParameters = new(transmit, receive, windowTx, windowRx)
        };
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
            await ApduChannels.ConnectAsync(new(factory, framing), TestContext.Current.CancellationToken));
        Assert.Equal(0, factory.ConnectCount);
    }

    private sealed class RecordingFactory(IByteTransport transport) : IByteTransportFactory
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

    private sealed class RecordingTransport : IByteTransport
    {
        public List<byte> Written { get; } = [];
        public int DisposeCount
        {
            get; private set;
        }

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
