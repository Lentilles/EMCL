using System.Collections.Immutable;

namespace EMCL.Protocol.Security;

/// <summary>Supplies the ACSE and HLS parameters used with an xDLMS protection protocol.</summary>
public interface ILnSecurityProtocol : IApduProtection
{
    /// <summary>Gets the application-context object identifier's final arc.</summary>
    byte ApplicationContext
    {
        get;
    }

    /// <summary>Gets the ACSE authentication mechanism identifier.</summary>
    byte AuthenticationMechanism
    {
        get;
    }

    /// <summary>Gets the local system title for the AARQ.</summary>
    ImmutableArray<byte> LocalSystemTitle
    {
        get;
    }

    /// <summary>Gets the expected peer title for validation of the AARE.</summary>
    ImmutableArray<byte> PeerSystemTitle
    {
        get;
    }

    /// <summary>Gets the optional dedicated key to send inside the encrypted InitiateRequest.</summary>
    ImmutableArray<byte> DedicatedKey
    {
        get;
    }

    /// <summary>Creates fresh authentication data for the AARQ.</summary>
    byte[] CreateChallenge();

    /// <summary>Creates the local reply to the peer's HLS challenge.</summary>
    /// <param name="localChallenge">The authentication data sent in the AARQ.</param>
    /// <param name="peerChallenge">The authentication data received in the AARE.</param>
    byte[] CreateReply(ReadOnlySpan<byte> localChallenge, ReadOnlySpan<byte> peerChallenge);

    /// <summary>Verifies the peer's HLS reply and throws on failure.</summary>
    /// <param name="localChallenge">The authentication data sent in the AARQ.</param>
    /// <param name="peerChallenge">The authentication data received in the AARE.</param>
    /// <param name="reply">The octets returned by the peer's HLS ACTION.</param>
    void VerifyReply(ReadOnlySpan<byte> localChallenge, ReadOnlySpan<byte> peerChallenge, ReadOnlySpan<byte> reply);
}
