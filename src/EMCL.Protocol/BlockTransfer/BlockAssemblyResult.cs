using System.Collections.Immutable;
using EMCL.Core.Operations;

namespace EMCL.Protocol.BlockTransfer;

/// <summary>Reports GET block assembly progress, completed bytes, or a transfer error.</summary>
/// <param name="Complete">Whether the final block has been received successfully.</param>
/// <param name="Data">The raw block payload or complete assembled bytes.</param>
/// <param name="Error">The operation or decode error, if any.</param>
public sealed record BlockAssemblyResult(bool Complete, ImmutableArray<byte> Data, CosemResultCode Error = CosemResultCode.Success);
