namespace EMCL.Objects.Definitions;

/// <summary>Defines supported ExtendedRegister versions and member indices independently of profile wire types.</summary>
public static class ExtendedRegisterMembers
{
    /// <summary>Interface-class version 0.</summary>
    public const byte Version0 = 0;

    /// <summary>Defines ExtendedRegister attributes by their standard member index.</summary>
    public static class Attributes
    {
        /// <summary>The Value member index.</summary>
        public const sbyte Value = 2;
        /// <summary>The ScalerUnit member index.</summary>
        public const sbyte ScalerUnit = 3;
        /// <summary>The Status member index.</summary>
        public const sbyte Status = 4;
        /// <summary>The CaptureTime member index.</summary>
        public const sbyte CaptureTime = 5;
    }

    /// <summary>Defines ExtendedRegister methods by their standard member index.</summary>
    public static class Methods
    {
        /// <summary>The Reset member index.</summary>
        public const sbyte Reset = 1;
    }
}
