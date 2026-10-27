using System.Collections.Immutable;

namespace EMCL.Core.Values;

/// <summary>Preserves the twelve COSEM date-time octets, including special fields, deviation, and status.</summary>
public sealed record CosemDateTime
{
    /// <summary>Gets the twelve original COSEM date-time octets without normalizing special fields.</summary>
    public ImmutableArray<byte> Octets
    {
        get;
    }

    /// <summary>Copies an exactly twelve-octet COSEM date-time value.</summary>
    /// <param name="octets">The twelve raw COSEM date-time octets.</param>
    /// <exception cref="ArgumentException">The input does not contain exactly twelve octets.</exception>
    public CosemDateTime(ReadOnlySpan<byte> octets)
    {
        if (octets.Length != DlmsValueSizes.DateTime)
        {
            throw new ArgumentException("COSEM date-time has 12 octets.");
        }

        Octets = octets.ToArray().ToImmutableArray();
    }

    /// <summary>Gets the signed deviation in minutes to add to local time to obtain UTC; Int16.MinValue denotes an unspecified deviation.</summary>
    public short Deviation => (short)((Octets[CosemDateTimeFields.DeviationHigh] << 8) | Octets[CosemDateTimeFields.DeviationLow]);
    /// <summary>Gets the original clock-status octet.</summary>
    public byte Status => Octets[CosemDateTimeFields.Status];

    /// <summary>Converts fully specified date-time fields and deviation into a DateTimeOffset.</summary>
    /// <returns>The specified local date and time with an offset equal to the negative COSEM deviation.</returns>
    /// <exception cref="InvalidOperationException">Required fields or deviation are unspecified or contain special values.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The specified date or offset cannot be represented by DateTimeOffset.</exception>
    public DateTimeOffset ToDateTimeOffset()
    {
        var b = Octets;
        int year = (b[CosemDateTimeFields.YearHigh] << 8) | b[CosemDateTimeFields.YearLow];
        if (year is 0 or > 9999 || b[CosemDateTimeFields.Month] is 0 or > 12 || b[CosemDateTimeFields.Day] is 0 or > 31 || b[CosemDateTimeFields.Hour] > 23 || b[CosemDateTimeFields.Minute] > 59 || b[CosemDateTimeFields.Second] > 59 || b[CosemDateTimeFields.Hundredths] > 99 || Deviation == CosemDateTimeFields.UnspecifiedDeviation)
        {
            throw new InvalidOperationException("Special or unspecified fields cannot be converted to DateTimeOffset.");
        }

        return new DateTimeOffset(year, b[CosemDateTimeFields.Month], b[CosemDateTimeFields.Day], b[CosemDateTimeFields.Hour], b[CosemDateTimeFields.Minute], b[CosemDateTimeFields.Second], TimeSpan.FromMinutes(-Deviation)).AddMilliseconds(b[CosemDateTimeFields.Hundredths] * CosemDateTimeFields.MillisecondsPerHundredth);
    }

    /// <summary>Creates COSEM date-time octets, truncating sub-hundredth fractions and preserving the supplied status.</summary>
    /// <param name="value">The value to convert, validate, store, or encode.</param>
    /// <param name="status">The clock-status octet.</param>
    /// <returns>A copied COSEM representation with hundredth-second precision.</returns>
    public static CosemDateTime FromDateTimeOffset(DateTimeOffset value, byte status = 0)
    {
        short deviation = checked((short)-value.Offset.TotalMinutes);
        return new(
            [
                (byte)(value.Year >> 8),
                (byte)value.Year,
                (byte)value.Month,
                (byte)value.Day,
                (byte)(value.DayOfWeek == DayOfWeek.Sunday ? 7 : (int)value.DayOfWeek),
                (byte)value.Hour,
                (byte)value.Minute,
                (byte)value.Second,
                (byte)(value.Millisecond / CosemDateTimeFields.MillisecondsPerHundredth),
                (byte)(deviation >> 8),
                (byte)deviation,
                status
        ]);
    }

    /// <summary>Returns a DLMS value tagged as date-time rather than octet-string.</summary>
    /// <returns>A value tagged as DlmsType.DateTime.</returns>
    public DlmsValue ToValue() => new(DlmsType.DateTime, Octets);
    /// <summary>Copies date-time octets from a date-time or twelve-octet octet-string value.</summary>
    /// <param name="value">The value to convert, validate, store, or encode.</param>
    /// <returns>A copied COSEM date-time representation.</returns>
    public static CosemDateTime FromValue(DlmsValue value)
    {
        if (value.Type is not (DlmsType.DateTime or DlmsType.OctetString))
        {
            throw new ArgumentException("Expected date-time.");
        }

        return new(value.Bytes.AsSpan());
    }
}
