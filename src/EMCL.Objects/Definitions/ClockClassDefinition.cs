using EMCL.Core.Descriptors;
using EMCL.Core.Objects;
using EMCL.Core.Values;

namespace EMCL.Objects.Definitions;

/// <summary>Constructs reusable Clock class metadata.</summary>
public static class ClockClassDefinition
{
    private const int MaximumDaylightSavingsDeviationMinutes = 120;
    private const int MinimumClockBase = 0;
    private const int MaximumClockBase = 5;

    /// <summary>Creates an independent, versioned class descriptor.</summary>
    /// <param name="timestampType">The date-time wire tag, either octet-string or date-time.</param>
    /// <param name="minimumZone">The minimum permitted UTC deviation in minutes.</param>
    /// <param name="maximumZone">The maximum permitted UTC deviation in minutes.</param>
    /// <returns>The class descriptor with the supplied member definitions.</returns>
    public static CosemClassDescriptor Create(DlmsType timestampType, int minimumZone, int maximumZone)
    {
        DefinitionFactory.RequireTimestampType(timestampType);
        return DefinitionFactory.Class(CosemClassIds.Clock, ClockMembers.Version0, "Clock",
            [DefinitionFactory.LogicalName(), new(ClockMembers.Attributes.Time, "time", timestampType, Constraint: DefinitionConstraints.DateTime),
                new(ClockMembers.Attributes.TimeZone, "time_zone", DlmsType.Int16, Constraint: value => Range(value, minimumZone, maximumZone)),
                new(ClockMembers.Attributes.Status, "status", DlmsType.UInt8),
                new(ClockMembers.Attributes.DaylightSavingsBegin, "daylight_savings_begin", timestampType, Constraint: DefinitionConstraints.DateTime),
                new(ClockMembers.Attributes.DaylightSavingsEnd, "daylight_savings_end", timestampType, Constraint: DefinitionConstraints.DateTime),
                new(ClockMembers.Attributes.DaylightSavingsDeviation, "daylight_savings_deviation", DlmsType.Int8, Constraint: value => Range(value, -MaximumDaylightSavingsDeviationMinutes, MaximumDaylightSavingsDeviationMinutes)),
                new(ClockMembers.Attributes.DaylightSavingsEnabled, "daylight_savings_enabled", DlmsType.Boolean),
                new(ClockMembers.Attributes.ClockBase, "clock_base", DlmsType.Enum, Constraint: value => Range(value, MinimumClockBase, MaximumClockBase))],
            [new(ClockMembers.Methods.AdjustToQuarter, "adjust_to_quarter", DlmsType.Int8), new(ClockMembers.Methods.AdjustToMeasuringPeriod, "adjust_to_measuring_period", DlmsType.Int8),
                new(ClockMembers.Methods.AdjustToMinute, "adjust_to_minute", DlmsType.Int8), new(ClockMembers.Methods.AdjustToPresetTime, "adjust_to_preset_time", DlmsType.Int8),
                new(ClockMembers.Methods.PresetAdjustingTime, "preset_adjusting_time", DlmsType.Structure), new(ClockMembers.Methods.ShiftTime, "shift_time", DlmsType.Int16)]);
    }

    private static string? Range(DlmsValue value, int minimum, int maximum)
        => value.AsInt64() >= minimum && value.AsInt64() <= maximum ? null : "Value is outside the allowed range.";
}
