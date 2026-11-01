namespace EMCL.Examples.AssociationDump.Configuration;

internal sealed record TcpConnectionSettings
{
    public required string Host
    {
        get; init;
    }
    public required int TcpPort
    {
        get; init;
    }
    public double ConnectTimeoutSeconds { get; init; } = 10;
}
