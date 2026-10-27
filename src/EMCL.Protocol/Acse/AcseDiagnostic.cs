namespace EMCL.Protocol.Acse;

/// <summary>Preserves the BER diagnostic-source tag and one-octet ACSE diagnostic code.</summary>
/// <param name="SourceTag">The service-user or service-provider diagnostic choice tag.</param>
/// <param name="Code">The raw diagnostic code interpreted within that source.</param>
public sealed record AcseDiagnostic(byte SourceTag, byte Code);
