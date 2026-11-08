using EMCL.Core.Operations;

namespace EMCL.Protocol.Apdu;

/// <summary>Acknowledges a SET block or terminates the transfer with a data-access result.</summary>
/// <param name="InvokeIdAndPriority">The unchanged invoke octet.</param>
/// <param name="BlockNumber">The acknowledged block number.</param>
/// <param name="Result">Null for an intermediate acknowledgement; otherwise the final result, including an early rejection.</param>
public sealed record SetBlockResponse(byte InvokeIdAndPriority, uint BlockNumber, CosemResultCode? Result = null) : DlmsApdu;
