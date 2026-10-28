using System.Collections.Immutable;
using System.Security.Cryptography;
using EMCL.Client.Exceptions;
using EMCL.Core.Values;
using EMCL.Protocol.Acse;

namespace EMCL.Client.Association;

internal static class HighAesAuthentication
{
    internal const int KeyLength = LnAssociationOptions.HighSecretLength;
    internal const int ChallengeLength = 16;

    internal static byte[] NewChallenge() => RandomNumberGenerator.GetBytes(ChallengeLength);

    internal static byte[] Encrypt(ReadOnlySpan<byte> secret, ReadOnlySpan<byte> challenge)
    {
        if (secret.Length != KeyLength || challenge.Length != ChallengeLength)
        {
            throw new DlmsProtocolException("High authentication requires a 16-byte secret and challenge.");
        }

        using var aes = Aes.Create();
        aes.Key = secret.ToArray();
        return aes.EncryptEcb(challenge, PaddingMode.None);
    }

    internal static ReadOnlySpan<byte> RequireChallenge(ImmutableArray<byte>? value, AcseApdu response)
    {
        if (value is not { } challenge || challenge.Length != ChallengeLength)
        {
            throw new DlmsAssociationException("A 16-byte HLS challenge is required.", response);
        }

        return challenge.AsSpan();
    }

    internal static void Verify(DlmsValue? value, ReadOnlySpan<byte> expected, AcseApdu response)
    {
        if (value is not { Type: DlmsType.OctetString } || value.Bytes.Length != expected.Length
            || !CryptographicOperations.FixedTimeEquals(value.Bytes.AsSpan(), expected))
        {
            throw new DlmsAssociationException("The server HLS challenge response is invalid.", response);
        }
    }
}
