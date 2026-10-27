using System.Collections.Immutable;

namespace EMCL.Core.Values;

/// <summary>Combines a shared type description with compact-array rows represented as DLMS values.</summary>
/// <param name="Description">The shared description of each compact-array row.</param>
/// <param name="Rows">The decoded row values matching the shared description.</param>
public sealed record DlmsCompactArray(CompactTypeDescription Description, ImmutableArray<DlmsValue> Rows);
