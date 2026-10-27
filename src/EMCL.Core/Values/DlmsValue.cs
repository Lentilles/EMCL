using System.Collections.Immutable;

namespace EMCL.Core.Values;

/// <summary>Stores a DLMS wire type and its payload, preserving the distinction between scalar and collection types.</summary>
/// <remarks>The payload representation must match Type. Numeric wire-range validation is performed during encoding. Date-time octets are preserved without implicit timezone normalization.</remarks>
public sealed record DlmsValue
{
    /// <summary>Gets the original DLMS Data choice tag.</summary>
    public DlmsType Type
    {
        get;
    }
    /// <summary>Gets the payload, using immutable arrays for copied byte sequences and value collections.</summary>
    public object? Value
    {
        get;
    }

    /// <summary>Creates a tagged DLMS value and copies mutable byte arrays and value collections.</summary>
    /// <param name="type">The exact DLMS wire tag.</param>
    /// <param name="value">The CLR payload for the supplied wire type; mutable byte arrays and value collections are copied.</param>
    public DlmsValue(DlmsType type, object? value)
    {
        Type = type;
        Value = value switch
        {
            byte[] bytes => bytes.ToImmutableArray(),
            IEnumerable<DlmsValue> items => items.ToImmutableArray(),
            _ => value
        };
    }

    /// <summary>Gets the shared null-data value.</summary>
    public static DlmsValue Null { get; } = new(DlmsType.Null, null);

    /// <summary>Creates an array value from a copied sequence of values.</summary>
    /// <param name="items">The ordered values to copy into the collection.</param>
    public static DlmsValue Array(params DlmsValue[] items) => new(DlmsType.Array, items);
    /// <summary>Creates a structure value from a copied sequence of fields.</summary>
    /// <param name="items">The ordered values to copy into the collection.</param>
    public static DlmsValue Structure(params DlmsValue[] items) => new(DlmsType.Structure, items);
    /// <summary>Creates an octet-string value by copying the supplied bytes.</summary>
    /// <param name="bytes">The bytes to copy or decode.</param>
    public static DlmsValue Octets(ReadOnlySpan<byte> bytes) => new(DlmsType.OctetString, bytes.ToArray());
    /// <summary>Creates a tagged numeric value from a signed 64-bit payload; encoding validates the target wire range.</summary>
    /// <param name="type">The exact DLMS wire tag.</param>
    /// <param name="value">The value to convert, validate, store, or encode.</param>
    public static DlmsValue Number(DlmsType type, long value) => new(type, value);
    /// <summary>Gets the immutable array or structure fields; throws when the payload is not a value collection.</summary>
    /// <exception cref="InvalidOperationException">The payload is not a collection of DLMS values.</exception>
    public ImmutableArray<DlmsValue> Items => Value is ImmutableArray<DlmsValue> items ? items : throw new InvalidOperationException("Value is not a collection.");
    /// <summary>Gets the immutable octets; throws when the payload is not a byte sequence.</summary>
    /// <exception cref="InvalidOperationException">The payload is not an immutable octet sequence.</exception>
    public ImmutableArray<byte> Bytes => Value is ImmutableArray<byte> bytes ? bytes : throw new InvalidOperationException("Value is not a byte sequence.");

    /// <summary>Converts the payload to a signed 64-bit integer using invariant culture.</summary>
    public long AsInt64() => Convert.ToInt64(Value, System.Globalization.CultureInfo.InvariantCulture);
    /// <summary>Converts the payload to an unsigned 64-bit integer using invariant culture.</summary>
    public ulong AsUInt64() => Convert.ToUInt64(Value, System.Globalization.CultureInfo.InvariantCulture);
}
