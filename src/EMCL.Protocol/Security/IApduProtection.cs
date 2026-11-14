namespace EMCL.Protocol.Security;

/// <summary>Defines a protection protocol for complete xDLMS APDUs, independent of framing and transport.</summary>
/// <remarks>The application owns this instance and its keys. Implementations shared by sessions must support concurrent calls.</remarks>
public interface IApduProtection
{
    /// <summary>Returns the exact encoded size without reserving counters or changing protocol state.</summary>
    /// <remarks>For each tag the protected size must be at least the plaintext length and must be nondecreasing.</remarks>
    /// <param name="tag">The plaintext xDLMS APDU tag.</param>
    /// <param name="length">The complete plaintext APDU length.</param>
    /// <param name="established">Whether application association authentication has completed.</param>
    int GetProtectedLength(byte tag, int length, bool established);

    /// <summary>Protects one complete xDLMS APDU for transmission.</summary>
    /// <param name="apdu">The encoded plaintext xDLMS APDU.</param>
    /// <param name="established">Whether application association authentication has completed.</param>
    byte[] Protect(ReadOnlySpan<byte> apdu, bool established);

    /// <summary>Verifies and unwraps one complete APDU, throwing if it fails the protection or replay policy.</summary>
    /// <param name="apdu">The received xDLMS APDU.</param>
    /// <param name="established">Whether application association authentication has completed.</param>
    byte[] Unprotect(ReadOnlySpan<byte> apdu, bool established);
}
