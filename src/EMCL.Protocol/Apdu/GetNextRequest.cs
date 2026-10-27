namespace EMCL.Protocol.Apdu;

/// <summary>Requests the next block of an ongoing Logical Name GET transfer.</summary>
/// <param name="InvokeIdAndPriority">The original invoke-id-and-priority octet.</param>
/// <param name="BlockNumber">The 32-bit data block number.</param>
public sealed record GetNextRequest(byte InvokeIdAndPriority, uint BlockNumber) : DlmsApdu;
