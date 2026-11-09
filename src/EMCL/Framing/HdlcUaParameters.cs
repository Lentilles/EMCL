namespace EMCL.Framing;

/// <summary>Parameters explicitly present in UA, in the peer's directions. Null properties mean omitted parameters.</summary>
/// <param name="MaximumTransmitInformationBytes">The peer's transmitted information limit in bytes, including LLC; null if omitted.</param>
/// <param name="MaximumReceiveInformationBytes">The peer's received information limit in bytes, including LLC; null if omitted.</param>
/// <param name="TransmitWindowFrames">The peer's transmit window in frames; null if omitted.</param>
/// <param name="ReceiveWindowFrames">The peer's receive window in frames; null if omitted.</param>
public sealed record HdlcUaParameters(int? MaximumTransmitInformationBytes = null,
    int? MaximumReceiveInformationBytes = null, int? TransmitWindowFrames = null, int? ReceiveWindowFrames = null);
