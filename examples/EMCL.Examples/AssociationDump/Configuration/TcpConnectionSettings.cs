namespace EMCL.Examples.AssociationDump.Configuration;

internal sealed record TcpConnectionSettings
{
    public MeterTransport Transport { get; init; } = MeterTransport.WrapperTcp;
    public required string Host
    {
        get; init;
    }
    public required int TcpPort
    {
        get; init;
    }
    public ushort ClientWrapperPort
    {
        get; init;
    } = WrapperConnectionDefaults.ClientPort;
    public ushort ServerWrapperPort
    {
        get; init;
    } = WrapperConnectionDefaults.ServerPort;
    public byte HdlcClientAddress { get; init; } = HdlcConnectionDefaults.ClientAddress;
    public ushort HdlcServerLogicalAddress { get; init; } = HdlcConnectionDefaults.ServerLogicalAddress;
    public ushort? HdlcServerPhysicalAddress
    {
        get; init;
    }
    public bool HdlcExtendedAddress
    {
        get; init;
    }
    public double ConnectTimeoutSeconds { get; init; } = 10;
}
