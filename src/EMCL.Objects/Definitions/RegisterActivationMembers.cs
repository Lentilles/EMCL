namespace EMCL.Objects.Definitions;

/// <summary>Defines supported RegisterActivation versions and member indices independently of profile wire types.</summary>
public static class RegisterActivationMembers
{
    /// <summary>Interface-class version 0.</summary>
    public const byte Version0 = 0;

    /// <summary>Defines RegisterActivation attributes by their standard member index.</summary>
    public static class Attributes
    {
        /// <summary>The RegisterAssignment member index.</summary>
        public const sbyte RegisterAssignment = 2;
        /// <summary>The MaskList member index.</summary>
        public const sbyte MaskList = 3;
        /// <summary>The ActiveMask member index.</summary>
        public const sbyte ActiveMask = 4;
    }

    /// <summary>Defines RegisterActivation methods by their standard member index.</summary>
    public static class Methods
    {
        /// <summary>The AddRegister member index.</summary>
        public const sbyte AddRegister = 1;
        /// <summary>The AddMask member index.</summary>
        public const sbyte AddMask = 2;
        /// <summary>The DeleteMask member index.</summary>
        public const sbyte DeleteMask = 3;
    }
}
