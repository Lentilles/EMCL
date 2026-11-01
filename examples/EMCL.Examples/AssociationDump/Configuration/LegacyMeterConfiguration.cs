using EMCL.Core.Codecs;

namespace EMCL.Examples.AssociationDump.Configuration;

internal sealed record LegacyMeterConfiguration
{
    public required LegacyConnectionSettings Connection { get; init; }
    public required AssociationSettings Association { get; init; }
    public required OutputSettings Output { get; init; }
    public ProtocolLimits Limits { get; init; } = new();

    internal MeterConfiguration Upgrade()
    {
        ArgumentNullException.ThrowIfNull(Connection);
        return new()
        {
            Channel = new()
            {
                Connection = new()
                {
                    Host = Connection.Host,
                    TcpPort = Connection.TcpPort,
                    ConnectTimeoutSeconds = Connection.ConnectTimeoutSeconds
                },
                Framing = Connection.Transport switch
                {
                    MeterTransport.WrapperTcp => new WrapperFramingSettings
                    {
                        ClientPort = Connection.ClientWrapperPort,
                        ServerPort = Connection.ServerWrapperPort
                    },
                    MeterTransport.HdlcTcp => new HdlcFramingSettings
                    {
                        ClientAddress = Connection.HdlcClientAddress,
                        ServerLogicalAddress = Connection.HdlcServerLogicalAddress,
                        ServerPhysicalAddress = Connection.HdlcServerPhysicalAddress,
                        ExtendedAddress = Connection.HdlcExtendedAddress
                    },
                    _ => throw new ArgumentException("connection.transport must be WrapperTcp or HdlcTcp.")
                }
            },
            Association = Association,
            Output = Output,
            Limits = Limits
        };
    }
}

internal sealed record LegacyConnectionSettings
{
    public MeterTransport Transport { get; init; } = MeterTransport.WrapperTcp;
    public required string Host { get; init; }
    public required int TcpPort { get; init; }
    public ushort ClientWrapperPort { get; init; } = WrapperConnectionDefaults.ClientPort;
    public ushort ServerWrapperPort { get; init; } = WrapperConnectionDefaults.ServerPort;
    public byte HdlcClientAddress { get; init; } = HdlcConnectionDefaults.ClientAddress;
    public ushort HdlcServerLogicalAddress { get; init; } = HdlcConnectionDefaults.ServerLogicalAddress;
    public ushort? HdlcServerPhysicalAddress { get; init; }
    public bool HdlcExtendedAddress { get; init; }
    public double ConnectTimeoutSeconds { get; init; } = 10;
}
