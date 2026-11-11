using System.Net;
using System.Net.Sockets;
using EMCL.Core.Codecs;
using EMCL.Framing;
using EMCL.Framing.Hdlc.Frames;
using EMCL.Transport.Streams;

namespace EMCL.Tests.Transport;

public sealed partial class HdlcApduChannelTests
{
    [Fact]
    public Task SendsDiscAndClosesTcpConnection() => VerifyDisconnectClosesTcpAsync();

    [Fact]
    public Task ClosesTcpConnectionWhenDiscWriteFailsWithIOException() =>
        VerifyDisconnectClosesTcpAsync(new IOException("The peer disconnected."));

    [Fact]
    public Task ClosesTcpConnectionWhenBlockedDiscWriteTimesOut() =>
        VerifyDisconnectClosesTcpAsync(new TimeoutException("The DISC write timed out."));

    [Fact]
    public Task ClosesTcpConnectionBeforePropagatingUnexpectedDiscWriteFailure() =>
        VerifyDisconnectClosesTcpAsync(new InvalidOperationException("Unexpected transport failure."), propagateFailure: true);

    private static async Task VerifyDisconnectClosesTcpAsync(Exception? writeFailure = null, bool propagateFailure = false)
    {
        var token = TestContext.Current.CancellationToken;
        var timeout = TimeSpan.FromSeconds(5);
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        await using var tcp = await TcpByteTransport.ConnectAsync("127.0.0.1",
            ((IPEndPoint)listener.LocalEndpoint).Port, timeout, token);
        using var peer = await listener.AcceptTcpClientAsync(token);
        var stream = peer.GetStream();
        var local = HdlcAddress.Client(48);
        var remote = HdlcAddress.Server(1, 17);
        var codec = new HdlcCodec();
        var transport = new BlockingDisconnectTransport(tcp, codec.EncodeBytes(new(remote, local, HdlcControl.Disc, [])));
        var connecting = HdlcApduChannel.ConnectAsync(transport, local, remote, timeout, cancellationToken: token);
        Assert.Equal(HdlcControl.Snrm, (await ReadFrameAsync(stream, codec)).Control);
        await WriteFrameAsync(stream, codec, new(local, remote, HdlcControl.Ua, []));
        await using var channel = await connecting;

        var disposing = channel.DisposeAsync().AsTask();
        await transport.DisconnectStarted.Task.WaitAsync(timeout, token);
        if (writeFailure is null)
        {
            transport.DisconnectReady.SetResult();
        }
        else
        {
            transport.DisconnectReady.SetException(writeFailure);
        }

        var error = await Record.ExceptionAsync(() => disposing.WaitAsync(timeout, token));
        if (writeFailure is null)
        {
            Assert.Equal(HdlcControl.Disc, (await ReadFrameAsync(stream, codec)).Control);
        }

        using var eofDeadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        eofDeadline.CancelAfter(TimeSpan.FromMilliseconds(500));
        Assert.Equal(0, await stream.ReadAsync(new byte[1], eofDeadline.Token));
        Assert.Same(propagateFailure ? writeFailure : null, error);
    }

    private sealed class BlockingDisconnectTransport(IByteTransport inner, byte[] disconnectFrame) : IByteTransport
    {
        public TaskCompletionSource DisconnectStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource DisconnectReady { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            inner.ReadAsync(buffer, cancellationToken);

        public async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (buffer.Span.SequenceEqual(disconnectFrame))
            {
                DisconnectStarted.SetResult();
                await DisconnectReady.Task.WaitAsync(cancellationToken);
            }

            await inner.WriteAsync(buffer, cancellationToken);
        }

        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }
}
