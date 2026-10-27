using EMCL.Protocol.Apdu;

namespace EMCL.Client.Association;

/// <summary>Contains the immutable xDLMS context accepted for one association.</summary>
/// <param name="DlmsVersion">The negotiated DLMS version.</param>
/// <param name="Conformance">The negotiated service subset.</param>
/// <param name="ServerMaxReceivePduSize">The server receive limit, independent of the client limit; zero denotes no protocol limit.</param>
/// <param name="ClientMaxReceivePduSize">The advertised client receive limit; local resource limits always apply.</param>
public sealed record NegotiatedAssociation(byte DlmsVersion, DlmsConformance Conformance,
    ushort ServerMaxReceivePduSize, ushort ClientMaxReceivePduSize);
