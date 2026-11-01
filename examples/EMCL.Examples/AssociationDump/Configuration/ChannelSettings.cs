namespace EMCL.Examples.AssociationDump.Configuration;

internal sealed record ChannelSettings
{
    public required TcpConnectionSettings Connection { get; init; }
    public required FramingSettings Framing { get; init; }
}
