using EMCL.Client.Association;
using EMCL.Protocol.Apdu;

namespace EMCL.Examples.AssociationDump.Configuration;

internal sealed record AssociationSettings
{
    public required string ProfileId
    {
        get; init;
    }
    public required byte Version
    {
        get; init;
    }
    public required string LogicalName
    {
        get; init;
    }
    public required string LogicalDeviceId
    {
        get; init;
    }
    public required string AssociationId
    {
        get; init;
    }
    public ClientAuthentication Authentication { get; init; } = ClientAuthentication.None;
    public string? Password
    {
        get; init;
    }
    public string? PasswordHex
    {
        get; init;
    }
    public string? PasswordEnvironmentVariable
    {
        get; init;
    }
    public double RequestTimeoutSeconds { get; init; } = 30;
    public ushort MaxReceivePduSize { get; init; } = LnAssociationOptions.DefaultMaxReceivePduSize;
    public DlmsConformance Conformance { get; init; } = DlmsConformance.Get | DlmsConformance.BlockTransferWithGet;
    public bool IgnoreIdenticalObjectListEntries { get; init; }
}
