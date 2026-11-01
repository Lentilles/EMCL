using EMCL.Transport.Streams;

namespace EMCL.Examples.AssociationDump.Configuration;

internal sealed record TcpConnectionSettings : ConnectionSettings
{
    public required string Host
    {
        get; init;
    }
    public required int TcpPort
    {
        get; init;
    }
    internal override string Kind => "Tcp";
    internal override string Endpoint => $"{Host}:{TcpPort}";

    internal override void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Host);
        if (TcpPort is <= 0 or > ushort.MaxValue)
        {
            throw new ArgumentException("channel.connection.tcpPort must be a valid nonzero TCP port.");
        }
    }

    internal override IByteTransportFactory CreateFactory(TimeSpan timeout) => new TcpByteTransportFactory(Host, TcpPort, timeout);
}
