namespace EMCL.Examples.AssociationDump.Export;

internal sealed record AssociationSource(string Host, int TcpPort, string Transport,
    ushort? ClientWrapperPort, ushort? ServerWrapperPort, byte? HdlcClientAddress,
    ushort? HdlcServerLogicalAddress, ushort? HdlcServerPhysicalAddress,
    string ProfileId, byte AssociationVersion, string AssociationObisCode, string LogicalDeviceId,
    string AssociationId, string Authentication);
