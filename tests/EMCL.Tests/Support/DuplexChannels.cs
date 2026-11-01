using System.Threading.Channels;
using EMCL.Framing;
using EMCL.Transport.Channels;
using EMCL.Transport.Streams;

namespace EMCL.Tests.Support;

internal sealed class DuplexApduChannel(ChannelReader<byte[]> input, ChannelWriter<byte[]> output) : IApduChannel
{
    internal int DisposeCount
    {
        get; private set;
    }
    internal static (DuplexApduChannel Client, DuplexApduChannel Server) CreatePair()
    {
        var toServer = Channel.CreateUnbounded<byte[]>();
        var toClient = Channel.CreateUnbounded<byte[]>();
        return (new(toClient.Reader, toServer.Writer), new(toServer.Reader, toClient.Writer));
    }

    public async ValueTask<ReadOnlyMemory<byte>?> ReceiveAsync(CancellationToken cancellationToken = default)
    {
        if (!await input.WaitToReadAsync(cancellationToken))
        {
            return null;
        }

        return await input.ReadAsync(cancellationToken);
    }

    public ValueTask SendAsync(ReadOnlyMemory<byte> apdu, CancellationToken cancellationToken = default) =>
        output.WriteAsync(apdu.ToArray(), cancellationToken);

    public ValueTask DisposeAsync()
    {
        DisposeCount++;
        output.TryComplete();
        return ValueTask.CompletedTask;
    }
}

internal sealed class DuplexByteTransport(ChannelReader<byte[]> input, ChannelWriter<byte[]> output, int maximumChunk) : IByteTransport
{
    private byte[] current = [];
    private int position;

    internal static (IApduChannel Client, IApduChannel Server) CreateWrapperPair()
    {
        var toServer = Channel.CreateUnbounded<byte[]>();
        var toClient = Channel.CreateUnbounded<byte[]>();
        return (
            ApduChannels.Wrapper(new DuplexByteTransport(toClient.Reader, toServer.Writer, 1), 16, 1),
            ApduChannels.Wrapper(new DuplexByteTransport(toServer.Reader, toClient.Writer, 3), 1, 16));
    }

    public async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (position == current.Length)
        {
            if (!await input.WaitToReadAsync(cancellationToken))
            {
                return 0;
            }

            current = await input.ReadAsync(cancellationToken);
            position = 0;
        }

        int count = Math.Min(maximumChunk, Math.Min(buffer.Length, current.Length - position));
        current.AsMemory(position, count).CopyTo(buffer);
        position += count;
        return count;
    }

    public ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) =>
        output.WriteAsync(buffer.ToArray(), cancellationToken);

    public ValueTask DisposeAsync()
    {
        output.TryComplete();
        return ValueTask.CompletedTask;
    }
}
