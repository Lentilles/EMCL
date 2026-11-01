namespace EMCL.Transport.Streams;

/// <summary>Owns an open serial port and exposes binary asynchronous byte I/O.</summary>
/// <remarks>Canceling in-flight I/O closes the connection, including on platforms where SerialStream ignores
/// cancellation tokens. Create a new transport to reconnect. No IEC 62056-21 sign-on or baud-rate switching is performed.</remarks>
public sealed class SerialByteTransport : IByteTransport
{
    private readonly Stream stream;
    private readonly IDisposable owner;
    private readonly Action? abortPendingIo;
    private int disposed;

    internal SerialByteTransport(Stream stream, IDisposable owner, Action? abortPendingIo = null)
    {
        this.stream = stream;
        this.owner = owner;
        this.abortPendingIo = abortPendingIo;
    }

    /// <summary>Opens a serial port within the supplied deadline.</summary>
    /// <remarks>Native port opening cannot be interrupted. If it completes after cancellation or timeout,
    /// the port is disposed instead of being returned to the caller.</remarks>
    /// <param name="settings">Serial port and line settings.</param>
    /// <param name="timeout">Maximum time allowed to open the port.</param>
    /// <param name="cancellationToken">Cancels opening the connection.</param>
    /// <returns>A transport that owns the open serial port.</returns>
    public static Task<SerialByteTransport> ConnectAsync(SerialPortSettings settings, TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.Validate();
        return OpenAsync(() =>
        {
            var port = settings.CreatePort();
            try
            {
                port.Open();
                return new SerialByteTransport(port.BaseStream, port, () =>
                {
                    // Cancel queued output before Close flushes it, especially if CTS never arrives.
                    port.DiscardOutBuffer();
                    port.DiscardInBuffer();
                });
            }
            catch
            {
                port.Dispose();
                throw;
            }
        }, timeout, cancellationToken);
    }

    internal static async Task<SerialByteTransport> OpenAsync(Func<SerialByteTransport> open, TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        if (timeout <= TimeSpan.Zero || timeout.TotalMilliseconds > uint.MaxValue - 1)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout));
        }

        cancellationToken.ThrowIfCancellationRequested();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);
        var openingToken = deadline.Token;
        var opening = Task.Run(() =>
        {
            openingToken.ThrowIfCancellationRequested();
            return open();
        }, CancellationToken.None);
        try
        {
            return await opening.WaitAsync(openingToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _ = Task.Run(() => DisposeLateConnectionAsync(opening), CancellationToken.None);
            if (exception is OperationCanceledException && !cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException("The serial port opening deadline expired.", exception);
            }

            throw;
        }
    }

    private static async Task DisposeLateConnectionAsync(Task<SerialByteTransport> opening)
    {
        try
        {
            var transport = await opening.ConfigureAwait(false);
            await transport.DisposeAsync().ConfigureAwait(false);
        }
        catch
        {
            // Observe failed opening or cleanup after the caller has already received its error.
        }
    }

    /// <inheritdoc />
    public async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        cancellationToken.ThrowIfCancellationRequested();
        if (buffer.IsEmpty)
        {
            return 0;
        }

        using var registration = cancellationToken.UnsafeRegister(static state => ((SerialByteTransport)state!).Abort(), this);
        try
        {
            int count = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            return count;
        }
        catch (Exception exception) when (cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException("The serial read was canceled and the port was closed.", exception, cancellationToken);
        }
    }

    /// <inheritdoc />
    public async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        cancellationToken.ThrowIfCancellationRequested();
        if (buffer.IsEmpty)
        {
            return;
        }

        using var registration = cancellationToken.UnsafeRegister(static state => ((SerialByteTransport)state!).Abort(), this);
        try
        {
            await stream.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
        }
        catch (Exception exception) when (cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException("The serial write was canceled and the port was closed.", exception, cancellationToken);
        }
    }

    private void Abort()
    {
        try
        {
            Close(abort: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            // A disconnected device may report an error while its handle is being closed.
        }
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        Close(abort: false);
        return ValueTask.CompletedTask;
    }

    private void Close(bool abort)
    {
        if (Interlocked.Exchange(ref disposed, 1) == 0)
        {
            try
            {
                if (abort)
                {
                    abortPendingIo?.Invoke();
                }
            }
            finally
            {
                owner.Dispose();
            }
        }
    }
}
