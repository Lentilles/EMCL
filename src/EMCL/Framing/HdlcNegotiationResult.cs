using System.Collections.Immutable;

namespace EMCL.Framing;

/// <summary>An immutable snapshot of SNRM/UA negotiation, with explicit byte and frame units.</summary>
/// <param name="Proposed">Local SNRM parameters; null means SNRM carried no parameter group and local defaults apply.</param>
/// <param name="Received">Peer-direction UA parameters; null means no group. Null individual values mean omitted parameters.</param>
/// <param name="Effective">Effective local-direction limits after matching local TX to peer RX and local RX to peer TX.</param>
/// <param name="UaInformation">Exact UA information bytes, preserving which individual parameters were present.</param>
public sealed record HdlcNegotiationResult(HdlcLinkParameters? Proposed, HdlcUaParameters? Received,
    HdlcLinkParameters Effective, ImmutableArray<byte> UaInformation);
