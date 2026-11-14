namespace EMCL.Protocol.Security;

/// <summary>Specifies which symmetric protections are applied to an APDU.</summary>
[Flags]
public enum ApduProtectionMode : byte
{
    /// <summary>No required protection, for matching a peer's configured receive policy.</summary>
    None = 0,
    /// <summary>Authenticate the data.</summary>
    Authentication = 0x10,
    /// <summary>Encrypt the data.</summary>
    Encryption = 0x20,
    /// <summary>Authenticate and encrypt the data.</summary>
    AuthenticationAndEncryption = Authentication | Encryption
}
