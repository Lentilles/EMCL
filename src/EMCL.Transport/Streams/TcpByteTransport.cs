using System.Net.Sockets;

namespace EMCL.Transport.Streams;

/// <summary>Owns a connected TCP socket and exposes it as an asynchronous byte transport.</summary>
public sealed class TcpByteTransport : IByteTransport
{
    private readonly TcpClient client;
    private readonly NetworkStream stream;
    private int disposed;

    private TcpByteTransport(TcpClient client)
    {
        this.client = client;
        stream = client.GetStream();
    }

    /// <summary>Connects to a TCP endpoint within the supplied timeout.</summary>
    /// <param name="host">DNS name or IP address of the remote endpoint.</param>
    /// <param name="port">Remote TCP port.</param>
    /// <param name="timeout">Maximum time allowed to connect.</param>
    /// <param name="cancellationToken">Cancels the connection attempt.</param>
    /// <returns>A transport that owns the connected socket.</returns>
    public static async Task<TcpByteTransport> ConnectAsync(string host, int port, TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(port);
        if (port > ushort.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(port));
        }

        if (timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout));
        }

        var client = new TcpClient();
        try
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(timeout);
            try
            {
                await client.ConnectAsync(host, port, deadline.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException("The TCP connection deadline expired.");
            }

            return new TcpByteTransport(client);
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    /// <inheritdoc />
    public ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        return stream.ReadAsync(buffer, cancellationToken);
    }

    /// <inheritdoc />
    public ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        return stream.WriteAsync(buffer, cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        try
        {
            await stream.DisposeAsync().ConfigureAwait(false);
        }
        finally
        {
            client.Dispose();
        }
    }
}
