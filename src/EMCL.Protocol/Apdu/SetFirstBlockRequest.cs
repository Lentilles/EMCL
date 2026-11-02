using System.Collections.Immutable;
using EMCL.Core.Operations;

namespace EMCL.Protocol.Apdu;

/// <summary>Starts one service-specific SET transfer with an attribute descriptor and raw A-XDR Data fragment.</summary>
/// <param name="InvokeIdAndPriority">The invoke octet retained for the entire transfer.</param>
/// <param name="Operation">The write descriptor, including optional selective access. Its value must be null; Data carries the encoded value.</param>
/// <param name="LastBlock">Whether this fragment completes the value.</param>
/// <param name="BlockNumber">The block number, starting at one.</param>
/// <param name="Data">Immutable fragment bytes, without an additional Data type tag.</param>
public sealed record SetFirstBlockRequest(byte InvokeIdAndPriority, CosemOperation Operation,
    bool LastBlock, uint BlockNumber, ImmutableArray<byte> Data) : DlmsApdu;
