namespace EMCL.Protocol.Acse;

/// <summary>Names standard ACSE service-provider association diagnostics.</summary>
public enum AcseServiceProviderDiagnostic : byte
{
    /// <summary>No diagnostic.</summary>
    None = 0,
    /// <summary>The provider supplied no specific reason.</summary>
    NoReasonGiven = 1,
    /// <summary>No common ACSE version is available.</summary>
    NoCommonAcseVersion = 2
}
