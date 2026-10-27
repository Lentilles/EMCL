using System.Collections.Immutable;

namespace EMCL.Protocol.Apdu;

/// <summary>Represents the xDLMS negotiation parameters carried by an InitiateRequest APDU.</summary>
/// <param name="DedicatedKey">The optional dedicated-key octets; an empty array denotes absence.</param>
/// <param name="ResponseAllowed">The explicitly supplied response-allowed flag, or null when the default is used.</param>
/// <param name="QualityOfService">The optional signed quality-of-service value.</param>
/// <param name="DlmsVersion">The proposed or negotiated DLMS version.</param>
/// <param name="Conformance">The 24-bit proposed or negotiated conformance bitmask.</param>
/// <param name="MaxReceivePduSize">The maximum APDU receive size advertised by the sender.</param>
public sealed record InitiateRequest(ImmutableArray<byte> DedicatedKey, bool? ResponseAllowed, sbyte? QualityOfService, byte DlmsVersion, uint Conformance, ushort MaxReceivePduSize) : DlmsApdu;
