namespace EMCL.Protocol.Apdu;

/// <summary>Acknowledges an ACTION request or response block.</summary>
/// <param name="InvokeIdAndPriority">The invoke ID and priority octet shared by the exchange.</param>
/// <param name="Response">Whether the acknowledgement is an ACTION response.</param>
/// <param name="BlockNumber">The block number acknowledged.</param>
public sealed record ActionNextBlock(byte InvokeIdAndPriority, bool Response, uint BlockNumber) : DlmsApdu;
