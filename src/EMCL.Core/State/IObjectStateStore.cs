using System.Diagnostics.CodeAnalysis;
using EMCL.Core.Values;

namespace EMCL.Core.State;

/// <summary>Stores current attribute values by their signed COSEM indices.</summary>
/// <remarks>Implementations used concurrently must provide their own synchronization. Compound operations are serialized by the standard object dispatcher.</remarks>
public interface IObjectStateStore
{
    /// <summary>Attempts to retrieve the current value of an attribute without synthesizing an absent value.</summary>
    /// <param name="index">The signed attribute or method index.</param>
    /// <param name="value">The stored attribute value on success; otherwise null.</param>
    /// <returns>True when the attribute exists; otherwise false, with an unspecified output value.</returns>
    bool TryGet(sbyte index, [MaybeNullWhen(false)] out DlmsValue value);
    /// <summary>Stores or replaces the value of an attribute.</summary>
    /// <param name="index">The signed attribute or method index.</param>
    /// <param name="value">The immutable tagged value to store for the attribute.</param>
    void Set(sbyte index, DlmsValue value);
}
