namespace EMCL.Objects.Definitions;

/// <summary>Defines supported Data versions and member indices independently of profile wire types.</summary>
public static class DataMembers
{
    /// <summary>Interface-class version 0.</summary>
    public const byte Version0 = 0;

    /// <summary>Defines Data attributes by their standard member index.</summary>
    public static class Attributes
    {
        /// <summary>The Value member index.</summary>
        public const sbyte Value = 2;
    }
}
