namespace EMCL.Objects.Definitions;

/// <summary>Defines supported DemandRegister versions and member indices independently of profile wire types.</summary>
public static class DemandRegisterMembers
{
    /// <summary>Interface-class version 0.</summary>
    public const byte Version0 = 0;

    /// <summary>Defines DemandRegister attributes by their standard member index.</summary>
    public static class Attributes
    {
        /// <summary>The CurrentAverageValue member index.</summary>
        public const sbyte CurrentAverageValue = 2;
        /// <summary>The LastAverageValue member index.</summary>
        public const sbyte LastAverageValue = 3;
        /// <summary>The ScalerUnit member index.</summary>
        public const sbyte ScalerUnit = 4;
        /// <summary>The Status member index.</summary>
        public const sbyte Status = 5;
        /// <summary>The CaptureTime member index.</summary>
        public const sbyte CaptureTime = 6;
        /// <summary>The StartTimeCurrent member index.</summary>
        public const sbyte StartTimeCurrent = 7;
        /// <summary>The Period member index.</summary>
        public const sbyte Period = 8;
        /// <summary>The NumberOfPeriods member index.</summary>
        public const sbyte NumberOfPeriods = 9;
    }

    /// <summary>Defines DemandRegister methods by their standard member index.</summary>
    public static class Methods
    {
        /// <summary>The Reset member index.</summary>
        public const sbyte Reset = 1;
        /// <summary>The NextPeriod member index.</summary>
        public const sbyte NextPeriod = 2;
    }
}
