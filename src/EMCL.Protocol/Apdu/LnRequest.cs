using EMCL.Core.Operations;

namespace EMCL.Protocol.Apdu;

/// <summary>Represents a normal Logical Name GET, SET, or ACTION request.</summary>
/// <param name="InvokeIdAndPriority">The original invoke-id-and-priority octet.</param>
/// <param name="Operation">The normal LN operation carried by the request.</param>
public sealed record LnRequest(byte InvokeIdAndPriority, CosemOperation Operation) : DlmsApdu;
