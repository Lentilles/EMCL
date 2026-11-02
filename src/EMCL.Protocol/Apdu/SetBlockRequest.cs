using System.Collections.Immutable;

namespace EMCL.Protocol.Apdu;

/// <summary>Continues one SET transfer without repeating the attribute descriptor.</summary>
/// <param name="InvokeIdAndPriority">The unchanged invoke octet.</param>
/// <param name="LastBlock">Whether this fragment completes the value.</param>
/// <param name="BlockNumber">The next sequential block number.</param>
/// <param name="Data">Immutable fragment of the original A-XDR Data encoding.</param>
public sealed record SetBlockRequest(byte InvokeIdAndPriority, bool LastBlock, uint BlockNumber,
    ImmutableArray<byte> Data) : DlmsApdu;
