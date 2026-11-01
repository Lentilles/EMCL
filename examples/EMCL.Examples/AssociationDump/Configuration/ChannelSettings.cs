namespace EMCL.Examples.AssociationDump.Configuration;

internal sealed record ChannelSettings
{
    public required ConnectionSettings Connection
    {
        get; init;
    }
    public required FramingSettings Framing
    {
        get; init;
    }
}
