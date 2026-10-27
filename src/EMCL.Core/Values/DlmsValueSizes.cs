namespace EMCL.Core.Values;

/// <summary>Defines the fixed encoded octet counts of COSEM date and time values.</summary>
public static class DlmsValueSizes
{
    /// <summary>The complete COSEM date-time length.</summary>
    public const int DateTime = 12;
    /// <summary>The COSEM date length.</summary>
    public const int Date = 5;
    /// <summary>The COSEM time length.</summary>
    public const int Time = 4;
}
