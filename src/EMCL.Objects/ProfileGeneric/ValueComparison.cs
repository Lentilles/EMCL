using System.Globalization;
using EMCL.Core.Values;

namespace EMCL.Objects.ProfileGeneric;

/// <summary>Compares supported DLMS scalar values for profile ordering and selective range access.</summary>
internal static class ValueComparison
{
    /// <summary>Determines whether a DLMS tag denotes a supported arithmetic scalar.</summary>
    /// <param name="type">The exact DLMS wire tag.</param>
    public static bool IsNumeric(DlmsType type) => type is DlmsType.Int8 or DlmsType.Int16 or DlmsType.Int32 or DlmsType.Int64 or DlmsType.UInt8 or DlmsType.UInt16 or DlmsType.UInt32 or DlmsType.UInt64 or DlmsType.Float32 or DlmsType.Float64 or >= DlmsType.DeltaInt8 and <= DlmsType.DeltaUInt32;
    /// <summary>Converts a supported numeric payload to Decimal for ordering without losing 64-bit integer precision.</summary>
    /// <param name="value">The value to convert, validate, store, or encode.</param>
    public static decimal Numeric(DlmsValue value)
    {
        if (!IsNumeric(value.Type))
        {
            throw new ArgumentException("Expected a numeric sort value.");
        }

        return Convert.ToDecimal(value.Value, CultureInfo.InvariantCulture);
    }

    /// <summary>Compares compatible numeric, string, date-time, or octet values for profile selection and ordering.</summary>
    /// <param name="a">The first value to compare.</param>
    /// <param name="b">The second value to compare.</param>
    public static int Compare(DlmsValue a, DlmsValue b)
    {
        if (IsNumeric(a.Type) && IsNumeric(b.Type))
        {
            return Numeric(a).CompareTo(Numeric(b));
        }

        if (a.Type != b.Type)
        {
            throw new ArgumentException("Incompatible comparison types.");
        }

        if (a.Type is DlmsType.DateTime || a.Type == DlmsType.OctetString && a.Bytes.Length == DlmsValueSizes.DateTime && b.Bytes.Length == DlmsValueSizes.DateTime)
        {
            return CosemDateTime.FromValue(a).ToDateTimeOffset().CompareTo(CosemDateTime.FromValue(b).ToDateTimeOffset());
        }

        if (a.Type is DlmsType.OctetString or DlmsType.Date or DlmsType.Time)
        {
            return a.Bytes.AsSpan().SequenceCompareTo(b.Bytes.AsSpan());
        }

        if (a.Type is DlmsType.VisibleString or DlmsType.Utf8String)
        {
            return string.CompareOrdinal((string)a.Value!, (string)b.Value!);
        }

        if (a.Type == DlmsType.Enum)
        {
            return a.AsInt64().CompareTo(b.AsInt64());
        }

        throw new ArgumentException("Value cannot be ordered.");
    }
}
