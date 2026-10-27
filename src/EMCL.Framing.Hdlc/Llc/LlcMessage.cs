using System.Collections.Immutable;

namespace EMCL.Framing.Hdlc.Llc;

/// <summary>Represents an LLC command or response and its complete application payload.</summary>
/// <param name="Response">Whether the LLC header denotes a response.</param>
/// <param name="Apdu">The complete application payload.</param>
/// <param name="Broadcast">Whether the LLC destination is the broadcast LSAP.</param>
public sealed record LlcMessage(bool Response, ImmutableArray<byte> Apdu, bool Broadcast = false);
