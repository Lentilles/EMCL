using EMCL.Framing;
using EMCL.Framing.Wrapper;
using EMCL.Messaging;
using EMCL.Transport.Channels;
using EMCL.Transport.Streams;

namespace EMCL.Tests.Transport;

public sealed class TransportLifetimeTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StreamOwnershipIsRespectedWhenTheAdapterIsDisposed(bool leaveOpen)
    {
        using var stream = new MemoryStream();
        var transport = new StreamByteTransport(stream, leaveOpen);
        await transport.DisposeAsync();
        await transport.DisposeAsync();
        Assert.Equal(leaveOpen, stream.CanRead);
        await Assert.ThrowsAsync<ObjectDisposedException>(async () => await transport.ReadAsync(new byte[1], TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task FramedChannelDisposesItsTransportExactlyOnce()
    {
        var transport = new TrackingTransport();
        var channel = new FramedApduChannel<WrapperFrame>(transport, new WrapperCodec(), new WrapperApduAdapter(16, 1));
        await channel.DisposeAsync();
        await channel.DisposeAsync();
        Assert.Equal(1, transport.DisposeCount);
        await Assert.ThrowsAsync<ObjectDisposedException>(async () => await channel.ReceiveAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task EndpointDisposesItsChannelExactlyOnce()
    {
        var channel = new TrackingChannel();
        var endpoint = new DlmsEndpoint(channel);
        await endpoint.DisposeAsync();
        await endpoint.DisposeAsync();
        Assert.Equal(1, channel.DisposeCount);
        await Assert.ThrowsAsync<ObjectDisposedException>(async () => await endpoint.ReceiveAsync(TestContext.Current.CancellationToken));
    }

    private sealed class TrackingTransport : IByteTransport
    {
        public int DisposeCount
        {
            get; private set;
        }

        public ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => ValueTask.FromResult(0);
        public ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask DisposeAsync()
        {
            DisposeCount++;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class TrackingChannel : IApduChannel
    {
        public int DisposeCount
        {
            get; private set;
        }

        public ValueTask<ReadOnlyMemory<byte>?> ReceiveAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult<ReadOnlyMemory<byte>?>(null);
        public ValueTask SendAsync(ReadOnlyMemory<byte> apdu, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask DisposeAsync()
        {
            DisposeCount++;
            return ValueTask.CompletedTask;
        }
    }
}
