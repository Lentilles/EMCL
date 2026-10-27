using System.Collections.Immutable;
using EMCL.Core.Values;

namespace EMCL.Objects.Internal;

internal static class Schema
{
    public static ImmutableArray<DlmsValue> Collection(DlmsValue value, DlmsType type) => value.Type == type ? value.Items : throw new FormatException($"Expected {type}, got {value.Type}.");
    public static ImmutableArray<DlmsValue> Structure(DlmsValue value, int count)
    {
        var items = Collection(value, DlmsType.Structure);
        return items.Length == count ? items : throw new FormatException($"Expected structure with {count} fields.");
    }

    public static long Number(DlmsValue value, DlmsType type) => value.Type == type ? value.AsInt64() : throw new FormatException($"Expected {type}, got {value.Type}.");
}
