namespace EMCL.Objects.Definitions;

/// <summary>Defines supported AssociationLn versions and member indices independently of profile wire types.</summary>
public static class AssociationLnMembers
{
    /// <summary>Interface-class version 1.</summary>
    public const byte Version1 = 1;
    /// <summary>Interface-class version 3.</summary>
    public const byte Version3 = 3;

    /// <summary>Defines AssociationLn attributes by their standard member index.</summary>
    public static class Attributes
    {
        /// <summary>The ObjectList member index.</summary>
        public const sbyte ObjectList = 2;
        /// <summary>The AssociatedPartnersId member index.</summary>
        public const sbyte AssociatedPartnersId = 3;
        /// <summary>The ApplicationContextName member index.</summary>
        public const sbyte ApplicationContextName = 4;
        /// <summary>The XdlmsContextInfo member index.</summary>
        public const sbyte XdlmsContextInfo = 5;
        /// <summary>The AuthenticationMechanismName member index.</summary>
        public const sbyte AuthenticationMechanismName = 6;
        /// <summary>The Secret member index.</summary>
        public const sbyte Secret = 7;
        /// <summary>The AssociationStatus member index.</summary>
        public const sbyte AssociationStatus = 8;
        /// <summary>The SecuritySetupReference member index.</summary>
        public const sbyte SecuritySetupReference = 9;
        /// <summary>The UserList member index.</summary>
        public const sbyte UserList = 10;
        /// <summary>The CurrentUser member index.</summary>
        public const sbyte CurrentUser = 11;
    }

    /// <summary>Defines AssociationLn methods by their standard member index.</summary>
    public static class Methods
    {
        /// <summary>The ReplyToHlsAuthentication member index.</summary>
        public const sbyte ReplyToHlsAuthentication = 1;
        /// <summary>The ChangeHlsSecret member index.</summary>
        public const sbyte ChangeHlsSecret = 2;
        /// <summary>The AddObject member index.</summary>
        public const sbyte AddObject = 3;
        /// <summary>The RemoveObject member index.</summary>
        public const sbyte RemoveObject = 4;
        /// <summary>The AddUser member index.</summary>
        public const sbyte AddUser = 5;
        /// <summary>The RemoveUser member index.</summary>
        public const sbyte RemoveUser = 6;
    }
}
