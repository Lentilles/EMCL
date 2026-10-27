using System.Collections.Immutable;

namespace EMCL.Core.Values;

/// <summary>Describes a compact-array element type, including fixed-size arrays and structure fields.</summary>
/// <param name="Type">The non-null DLMS wire tag used for each value described by this node.</param>
/// <param name="Children">The structure field descriptions, or the single element description for an array.</param>
/// <param name="ElementCount">The fixed number of elements in an array description; unused for scalar and structure descriptions.</param>
public sealed record CompactTypeDescription(DlmsType Type, ImmutableArray<CompactTypeDescription> Children, ushort ElementCount = 0)
{
    /// <summary>Creates a type description without child descriptions.</summary>
    /// <param name="type">The exact DLMS wire tag.</param>
    public static CompactTypeDescription Scalar(DlmsType type) => new(type, []);
}
