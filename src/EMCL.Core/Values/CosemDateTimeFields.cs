namespace EMCL.Core.Values;

/// <summary>Defines offsets and special values in a twelve-octet COSEM date-time.</summary>
public static class CosemDateTimeFields
{
    /// <summary>The first year octet offset.</summary>
    public const int YearHigh = 0;
    /// <summary>The second year octet offset.</summary>
    public const int YearLow = 1;
    /// <summary>The month offset.</summary>
    public const int Month = 2;
    /// <summary>The day-of-month offset.</summary>
    public const int Day = 3;
    /// <summary>The day-of-week offset.</summary>
    public const int DayOfWeek = 4;
    /// <summary>The hour offset.</summary>
    public const int Hour = 5;
    /// <summary>The minute offset.</summary>
    public const int Minute = 6;
    /// <summary>The second offset.</summary>
    public const int Second = 7;
    /// <summary>The hundredths-of-a-second offset.</summary>
    public const int Hundredths = 8;
    /// <summary>The first deviation octet offset.</summary>
    public const int DeviationHigh = 9;
    /// <summary>The second deviation octet offset.</summary>
    public const int DeviationLow = 10;
    /// <summary>The clock-status offset.</summary>
    public const int Status = 11;
    /// <summary>The unspecified one-octet field marker.</summary>
    public const byte Unspecified = byte.MaxValue;
    /// <summary>The unspecified UTC deviation marker.</summary>
    public const short UnspecifiedDeviation = short.MinValue;
    /// <summary>The millisecond count in one hundredth of a second.</summary>
    public const int MillisecondsPerHundredth = 10;
}
