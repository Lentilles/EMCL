using EMCL.Core.Values;

namespace EMCL.Core.Operations;

/// <summary>Combines an access selector with its A-XDR parameter value.</summary>
/// <param name="Selector">The selective-access identifier.</param>
/// <param name="Parameters">The selector-specific DLMS parameter value.</param>
public sealed record SelectiveAccess(byte Selector, DlmsValue Parameters);
