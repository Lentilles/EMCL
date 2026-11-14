using System.Collections.Immutable;
using EMCL.Core.Operations;

namespace EMCL.Protocol.Apdu;

/// <summary>Represents an ACTION data block. Only the first request block carries the method descriptor.</summary>
/// <param name="InvokeIdAndPriority">The invoke ID and priority octet shared by the exchange.</param>
/// <param name="Response">Whether this is an ACTION response block.</param>
/// <param name="LastBlock">Whether this is the final block in its direction.</param>
/// <param name="BlockNumber">The numbered data block.</param>
/// <param name="Data">The fragment of the encoded invocation parameter or result.</param>
/// <param name="Method">The invocation descriptor on a first request block, with no attached value.</param>
public sealed record ActionBlock(byte InvokeIdAndPriority, bool Response, bool LastBlock, uint BlockNumber,
    ImmutableArray<byte> Data, CosemOperation? Method = null) : DlmsApdu;
