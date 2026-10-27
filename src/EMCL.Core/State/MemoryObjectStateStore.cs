using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using EMCL.Core.Values;

namespace EMCL.Core.State;

/// <summary>Stores immutable DLMS values in a thread-safe, in-memory attribute dictionary.</summary>
public sealed class MemoryObjectStateStore : IObjectStateStore
{
    private readonly ConcurrentDictionary<sbyte, DlmsValue> values = new();
    /// <inheritdoc />
    public bool TryGet(sbyte index, [MaybeNullWhen(false)] out DlmsValue value) => values.TryGetValue(index, out value!);
    /// <inheritdoc />
    public void Set(sbyte index, DlmsValue value) => values[index] = value;
}
