using EMCL.Core.Codecs;
using EMCL.Objects.Definitions;

namespace EMCL.Examples.AssociationDump.Configuration;

internal sealed record MeterConfiguration
{
    public CosemProfile Profile
    {
        get; init;
    }
    public required ChannelSettings Channel
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
