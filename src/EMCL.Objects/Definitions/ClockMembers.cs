namespace EMCL.Objects.Definitions;

/// <summary>Defines supported Clock versions and member indices independently of profile wire types.</summary>
public static class ClockMembers
{
    /// <summary>Interface-class version 0.</summary>
    public const byte Version0 = 0;

    /// <summary>Defines Clock attributes by their standard member index.</summary>
    public static class Attributes
    {
        /// <summary>The Time member index.</summary>
        public const sbyte Time = 2;
        /// <summary>The TimeZone member index.</summary>
        public const sbyte TimeZone = 3;
        /// <summary>The Status member index.</summary>
        public const sbyte Status = 4;
        /// <summary>The DaylightSavingsBegin member index.</summary>
        public const sbyte DaylightSavingsBegin = 5;
        /// <summary>The DaylightSavingsEnd member index.</summary>
        public const sbyte DaylightSavingsEnd = 6;
        /// <summary>The DaylightSavingsDeviation member index.</summary>
        public const sbyte DaylightSavingsDeviation = 7;
        /// <summary>The DaylightSavingsEnabled member index.</summary>
        public const sbyte DaylightSavingsEnabled = 8;
        /// <summary>The ClockBase member index.</summary>
        public const sbyte ClockBase = 9;
    }

    /// <summary>Defines Clock methods by their standard member index.</summary>
    public static class Methods
    {
        /// <summary>The AdjustToQuarter member index.</summary>
        public const sbyte AdjustToQuarter = 1;
        /// <summary>The AdjustToMeasuringPeriod member index.</summary>
        public const sbyte AdjustToMeasuringPeriod = 2;
        /// <summary>The AdjustToMinute member index.</summary>
        public const sbyte AdjustToMinute = 3;
        /// <summary>The AdjustToPresetTime member index.</summary>
        public const sbyte AdjustToPresetTime = 4;
        /// <summary>The PresetAdjustingTime member index.</summary>
        public const sbyte PresetAdjustingTime = 5;
        /// <summary>The ShiftTime member index.</summary>
        public const sbyte ShiftTime = 6;
    }
}
