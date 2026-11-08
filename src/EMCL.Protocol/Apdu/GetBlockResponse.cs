using System.Collections.Immutable;
using EMCL.Core.Operations;

namespace EMCL.Protocol.Apdu;

/// <summary>Represents one GET data block or its data-access error.</summary>
/// <param name="InvokeIdAndPriority">The original invoke-id-and-priority octet.</param>
/// <param name="LastBlock">Whether this is the final block of the transfer.</param>
/// <param name="BlockNumber">The 32-bit data block number.</param>
/// <param name="Data">The raw block payload or complete assembled bytes.</param>
/// <param name="Error">The optional data-access error; null denotes a raw-data block.</param>
public sealed record GetBlockResponse(byte InvokeIdAndPriority, bool LastBlock, uint BlockNumber, ImmutableArray<byte> Data, CosemResultCode? Error = null) : DlmsApdu;
