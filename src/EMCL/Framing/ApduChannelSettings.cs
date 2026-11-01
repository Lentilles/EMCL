using EMCL.Core.Codecs;
using EMCL.Transport.Streams;

namespace EMCL.Framing;

/// <summary>Defines one connection factory and one framing choice for a complete-APDU channel.</summary>
/// <param name="Connection">Opens the byte transport exactly once for this channel.</param>
/// <param name="Framing">The selected Wrapper or HDLC framing.</param>
/// <param name="Limits">Optional resource bounds for the channel.</param>
public sealed record ApduChannelSettings(IByteTransportFactory Connection, ApduFraming Framing,
    ProtocolLimits? Limits = null);
