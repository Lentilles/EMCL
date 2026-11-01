using System.IO.Ports;
using EMCL.Transport.Streams;

namespace EMCL.Tests.Transport;

public sealed class SerialByteTransportTests
{
    [Fact]
    public void RejectsInvalidLineSettingsBeforeOpeningAPort()
    {
        var valid = new SerialPortSettings("unused-port");
        var invalid = new[]
        {
            valid with { PortName = " " },
            valid with { BaudRate = 0 },
            valid with { DataBits = 4 },
            valid with { DataBits = 9 },
            valid with { Parity = (Parity)99 },
            valid with { StopBits = StopBits.None },
            valid with { StopBits = (StopBits)99 },
            valid with { Handshake = (Handshake)99 },
            valid with { Handshake = Handshake.RequestToSend, RtsEnable = true }
        };
        foreach (var settings in invalid)
        {
            Assert.ThrowsAny<ArgumentException>(settings.Validate);
        }

        using var port = (valid with
        {
            Parity = Parity.Even,
            DtrEnable = true
        }).CreatePort();
        Assert.Equal(Parity.Even, port.Parity);
        Assert.True(port.DtrEnable);
        Assert.Equal((byte)0, port.ParityReplace);
        Assert.False(port.DiscardNull);
    }

    [Fact]
    public async Task TransfersBinaryDataAndDisposesOwnedStreamExactlyOnce()
    {
        byte[] bytes = [0, 0x7e, 0xff, 0x11, 0x13];
        var stream = new ControlledSerialStream(bytes);
        var transport = new SerialByteTransport(stream, stream);
        byte[] buffer = new byte[bytes.Length];
        Assert.Equal(bytes.Length, await transport.ReadAsync(buffer, TestContext.Current.CancellationToken));
        Assert.Equal(bytes, buffer);
        await transport.WriteAsync(bytes, TestContext.Current.CancellationToken);
        Assert.Equal(bytes, stream.Written);
        await transport.DisposeAsync();
        await transport.DisposeAsync();
        Assert.Equal(1, stream.DisposeCount);
        await Assert.ThrowsAsync<ObjectDisposedException>(async () => await transport.ReadAsync(buffer, TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ObjectDisposedException>(async () => await transport.WriteAsync(bytes, TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task CancellationClosesPortAndDrainsIoEvenWhenStreamIgnoresToken(bool write, bool discardFails)
    {
        var stream = new ControlledSerialStream();
        bool aborted = false;
        await using var transport = new SerialByteTransport(stream, stream, () =>
        {
            aborted = true;
            if (discardFails)
            {
                throw new IOException("Device disconnected while discarding queued output.");
            }
        });
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var pending = write
            ? transport.WriteAsync(new byte[] { 1 }, cancellation.Token).AsTask()
            : transport.ReadAsync(new byte[1], cancellation.Token).AsTask();
        Assert.False(pending.IsCompleted);
        await cancellation.CancelAsync();
        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await pending.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
        Assert.Equal(cancellation.Token, error.CancellationToken);
        Assert.True(aborted);
        Assert.Equal(1, stream.DisposeCount);
        await Assert.ThrowsAsync<ObjectDisposedException>(async () => await transport.ReadAsync(new byte[1], TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AlreadyCanceledOperationDoesNotCloseOrReadThePort()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var stream = new ControlledSerialStream();
        await using var transport = new SerialByteTransport(stream, stream);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await transport.ReadAsync(new byte[1], cancellation.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await transport.WriteAsync(new byte[] { 1 }, cancellation.Token));
        Assert.Equal(0, stream.DisposeCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OpeningDeadlineDisposesAPortThatFinishesOpeningLate(bool callerCancels)
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var stream = new ControlledSerialStream();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var opening = SerialByteTransport.OpenAsync(() =>
        {
            started.SetResult();
            release.Task.GetAwaiter().GetResult();
            return new SerialByteTransport(stream, stream);
        }, callerCancels ? TimeSpan.FromSeconds(5) : TimeSpan.FromMilliseconds(200), cancellation.Token);
        try
        {
            await started.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            if (callerCancels)
            {
                await cancellation.CancelAsync();
                await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await opening);
            }
            else
            {
                await Assert.ThrowsAsync<TimeoutException>(async () => await opening);
            }
        }
        finally
        {
            release.TrySetResult();
        }

        await stream.Disposed.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.Equal(1, stream.DisposeCount);
    }

    [Fact]
    public async Task OpeningFailureIsPropagatedAndPreCanceledOpenDoesNotAccessThePort()
    {
        await Assert.ThrowsAsync<IOException>(async () => await SerialByteTransport.OpenAsync(
            () => throw new IOException("Cannot open port."), TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await new SerialByteTransportFactory(new("unused-port"), TimeSpan.FromSeconds(5)).ConnectAsync(cancellation.Token));
    }

    // Models a serial driver whose async operations do not honor cancellation tokens.
    private sealed class ControlledSerialStream(byte[]? input = null) : Stream
    {
        private readonly TaskCompletionSource<int> pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Disposed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal int DisposeCount
        {
            get; private set;
        }
        internal byte[]? Written
        {
            get; private set;
        }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException(); set => throw new NotSupportedException();
        }
        public override void Flush() => throw new NotSupportedException();
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (input is null)
            {
                return new(pending.Task);
            }

            int count = Math.Min(input.Length, buffer.Length);
            input.AsMemory(0, count).CopyTo(buffer);
            return ValueTask.FromResult(count);
        }

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            Written = buffer.ToArray();
            return input is null ? new(pending.Task) : ValueTask.CompletedTask;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                DisposeCount++;
                pending.TrySetException(new IOException("Port closed."));
                Disposed.TrySetResult();
            }

            base.Dispose(disposing);
        }
    }
}
