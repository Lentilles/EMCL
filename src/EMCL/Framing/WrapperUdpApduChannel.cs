using System.Buffers;
using System.Net;
using System.Net.Sockets;
using EMCL.Core.Codecs;
using EMCL.Framing.Wrapper;
using EMCL.Transport.Channels;

namespace EMCL.Framing;

/// <summary>Exchanges one DLMS Wrapper frame per UDP datagram using a connected UDP socket.</summary>
public sealed class WrapperUdpApduChannel : IApduChannel
{
    private readonly UdpClient client;
    private readonly WrapperCodec codec;
    private readonly WrapperApduAdapter adapter;
    private readonly bool leaveOpen;
    private int disposed;

    /// <summary>Wraps a UDP socket already connected to one remote endpoint.</summary>
    /// <param name="client">The UDP socket used for both directions.</param>
    /// <param name="localPort">The local DLMS wPort.</param>
    /// <param name="remotePort">The remote DLMS wPort.</param>
    /// <param name="limits">Optional Wrapper and APDU limits.</param>
    /// <param name="leaveOpen">Whether disposing the channel leaves the UDP socket open.</param>
    public WrapperUdpApduChannel(UdpClient client, ushort localPort, ushort remotePort,
        ProtocolLimits? limits = null, bool leaveOpen = false)
    {
        ArgumentNullException.ThrowIfNull(client);
        if (client.Client.RemoteEndPoint is not IPEndPoint)
        {
            throw new ArgumentException("The UDP socket must be connected to a remote endpoint.", nameof(client));
        }

        this.client = client;
        this.leaveOpen = leaveOpen;
        codec = new(limits);
        adapter = new(localPort, remotePort);
    }

    /// <inheritdoc />
    public async ValueTask<ReadOnlyMemory<byte>?> ReceiveAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        var datagram = await client.ReceiveAsync(cancellationToken).ConfigureAwait(false);
        var decoded = codec.Decode(new ReadOnlySequence<byte>(datagram.Buffer));
        if (decoded.Status != DecodeStatus.Done || decoded.Consumed != datagram.Buffer.Length || decoded.Value is not { } frame)
        {
            throw new InvalidDataException("The datagram does not contain exactly one complete Wrapper frame.");
        }

        return adapter.Accept(frame);
    }

    /// <inheritdoc />
    public async ValueTask SendAsync(ReadOnlyMemory<byte> apdu, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        byte[] bytes = codec.EncodeBytes(adapter.Frame(apdu).Single());
        await client.SendAsync(bytes.AsMemory(), cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) == 0 && !leaveOpen)
        {
            client.Dispose();
        }

        return ValueTask.CompletedTask;
    }
}
