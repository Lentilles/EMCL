using EMCL.Core.Codecs;

namespace EMCL.Examples.AssociationDump.Configuration;

internal sealed record MeterConfiguration
{
    public required TcpConnectionSettings Connection
    {
        get; init;
    }
    public required AssociationSettings Association
    {
        get; init;
    }
    public required OutputSettings Output
    {
        get; init;
    }
    public ProtocolLimits Limits { get; init; } = new();
}
