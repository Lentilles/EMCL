namespace EMCL.Protocol.Acse;

/// <summary>Defines the standard choices used by the convenience ACSE factories.</summary>
internal static class AcseDefaults
{
    /// <summary>The unciphered logical-name application context.</summary>
    internal const byte UncipheredLnContext = 1;
    /// <summary>The accepted association result.</summary>
    internal const byte Accepted = 0;
    /// <summary>The no-reason-given service-user diagnostic.</summary>
    internal const byte NoDiagnostic = 0;
    /// <summary>The normal release reason.</summary>
    internal const byte NormalRelease = 0;
    /// <summary>The unused-bit count for the authentication requirement bit-string.</summary>
    internal const byte AuthenticationUnusedBits = 7;
    /// <summary>The authentication functional-unit bit.</summary>
    internal const byte AuthenticationRequired = 0x80;
}
