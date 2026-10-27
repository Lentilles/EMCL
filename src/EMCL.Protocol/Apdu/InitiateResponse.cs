namespace EMCL.Protocol.Apdu;

/// <summary>Represents the xDLMS negotiation parameters carried by an InitiateResponse APDU.</summary>
/// <param name="QualityOfService">The optional signed quality-of-service value.</param>
/// <param name="DlmsVersion">The proposed or negotiated DLMS version.</param>
/// <param name="Conformance">The 24-bit proposed or negotiated conformance bitmask.</param>
/// <param name="MaxReceivePduSize">The maximum APDU receive size advertised by the sender.</param>
/// <param name="VaaName">The negotiated VAA name; 7 identifies Logical Name referencing.</param>
public sealed record InitiateResponse(sbyte? QualityOfService, byte DlmsVersion, uint Conformance, ushort MaxReceivePduSize, ushort VaaName = DlmsNegotiation.LogicalNameVaa) : DlmsApdu;
