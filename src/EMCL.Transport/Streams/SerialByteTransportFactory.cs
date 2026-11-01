namespace EMCL.Transport.Streams;

/// <summary>Opens a serial byte transport for a Wrapper or HDLC APDU channel.</summary>
/// <param name="Settings">Serial port and line settings.</param>
/// <param name="Timeout">Maximum time allowed to open the port.</param>
public sealed record SerialByteTransportFactory(SerialPortSettings Settings, TimeSpan Timeout) : IByteTransportFactory
{
    /// <inheritdoc />
    public async ValueTask<IByteTransport> ConnectAsync(CancellationToken cancellationToken = default) =>
        await SerialByteTransport.ConnectAsync(Settings, Timeout, cancellationToken).ConfigureAwait(false);
}
