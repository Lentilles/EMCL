namespace EMCL.Objects.Definitions;

/// <summary>Defines supported ProfileGeneric versions and member indices independently of profile wire types.</summary>
public static class ProfileGenericMembers
{
    /// <summary>Interface-class version 1.</summary>
    public const byte Version1 = 1;

    /// <summary>Defines ProfileGeneric attributes by their standard member index.</summary>
    public static class Attributes
    {
        /// <summary>The Buffer member index.</summary>
        public const sbyte Buffer = 2;
        /// <summary>The CaptureObjects member index.</summary>
        public const sbyte CaptureObjects = 3;
        /// <summary>The CapturePeriod member index.</summary>
        public const sbyte CapturePeriod = 4;
        /// <summary>The SortMethod member index.</summary>
        public const sbyte SortMethod = 5;
        /// <summary>The SortObject member index.</summary>
        public const sbyte SortObject = 6;
        /// <summary>The EntriesInUse member index.</summary>
        public const sbyte EntriesInUse = 7;
        /// <summary>The ProfileEntries member index.</summary>
        public const sbyte ProfileEntries = 8;
    }

    /// <summary>Defines ProfileGeneric methods by their standard member index.</summary>
    public static class Methods
    {
        /// <summary>The Reset member index.</summary>
        public const sbyte Reset = 1;
        /// <summary>The Capture member index.</summary>
        public const sbyte Capture = 2;
    }
}
