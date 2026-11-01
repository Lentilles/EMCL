using System.Text.Json.Serialization;

namespace EMCL.Examples.AssociationDump.Configuration;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(WrapperFramingSettings), "Wrapper")]
[JsonDerivedType(typeof(HdlcFramingSettings), "Hdlc")]
internal abstract record FramingSettings;

internal sealed record WrapperFramingSettings : FramingSettings
{
    public ushort ClientPort { get; init; } = WrapperConnectionDefaults.ClientPort;
    public ushort ServerPort { get; init; } = WrapperConnectionDefaults.ServerPort;
}

internal sealed record HdlcFramingSettings : FramingSettings
{
    public byte ClientAddress { get; init; } = HdlcConnectionDefaults.ClientAddress;
    public ushort ServerLogicalAddress { get; init; } = HdlcConnectionDefaults.ServerLogicalAddress;
    public ushort? ServerPhysicalAddress { get; init; }
    public bool ExtendedAddress { get; init; }
}
