using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Security.Cryptography;
using EMCL.Protocol.Apdu;
using EMCL.Protocol.Internal;

namespace EMCL.Protocol.Security;

/// <summary>Implements symmetric LN security with DLMS AES-GCM-128 or SPODES Kuznyechik CTR-CMAC.</summary>
/// <remarks>Use a distinct instance per association when configuring a dedicated key. Share durable counter storage across associations.</remarks>
public sealed class DlmsSecurityProtocol : ILnSecurityProtocol, ISecurityKeyTransfer
{
    private const int TitleSize = 8;
    private const int HeaderSize = 1 + sizeof(uint);
    private const int ChallengeSize = 16;
    private const int MinChallengeSize = 8;
    private const int MaxChallengeSize = 64;
    private const byte GeneralGlobal = (byte)ApduTag.GeneralGlobalCiphering;
    private const byte GeneralDedicated = (byte)ApduTag.GeneralDedicatedCiphering;
    private const byte ProtectionMask = (byte)ApduProtectionMode.AuthenticationAndEncryption;
    private const byte SuiteMask = 0x0f;
    private const byte UnsupportedControlMask = 0xc0;

    private readonly DlmsSecurityOptions options;
    private readonly string globalOutgoing;
    private readonly string globalIncoming;
    private readonly string hlsIncoming;
    private readonly string dedicatedOutgoing;
    private readonly string dedicatedIncoming;
    private readonly string masterOutgoing;
    private readonly string masterIncoming;
    private readonly int keySize;
    private readonly int tagSize;

    /// <summary>Validates the security material and configures protection for one peer.</summary>
    /// <param name="options">The keys, titles, policies and counter storage supplied by the caller.</param>
    public DlmsSecurityProtocol(DlmsSecurityOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(options.Counters);
        this.options = options;
        if (!Enum.IsDefined(options.Suite)
            || !Enum.IsDefined(options.OutgoingMode) || options.OutgoingMode == ApduProtectionMode.None
            || !Enum.IsDefined(options.MinimumIncomingMode))
        {
            throw new ArgumentException("Invalid security suite or protection policy.", nameof(options));
        }

        keySize = IsAes ? AesDlmsCipher.KeySize : KuznyechikDlmsCipher.KeySize;
        tagSize = IsAes ? AesDlmsCipher.TagSize : KuznyechikDlmsCipher.TagSize;
        if (options.LocalSystemTitle.Length != TitleSize || options.PeerSystemTitle.Length != TitleSize
            || options.LocalSystemTitle.AsSpan().SequenceEqual(options.PeerSystemTitle.AsSpan())
            || options.GlobalKey.Length != keySize
            || !options.DedicatedKey.IsDefaultOrEmpty && options.DedicatedKey.Length != keySize
            || !options.MasterKey.IsDefaultOrEmpty && options.MasterKey.Length != keySize
            || (IsAes ? options.AuthenticationKey.Length != keySize : !options.AuthenticationKey.IsDefaultOrEmpty))
        {
            throw new ArgumentException("The system titles or keys do not match the selected suite.", nameof(options));
        }

        globalOutgoing = Scope(options.GlobalKey, true);
        globalIncoming = Scope(options.GlobalKey, false);
        // A protected HLS ACTION contains an earlier counter inside a separately verified envelope.
        hlsIncoming = globalIncoming + ":hls";
        dedicatedOutgoing = Scope(options.DedicatedKey, true);
        dedicatedIncoming = Scope(options.DedicatedKey, false);
        masterOutgoing = Scope(options.MasterKey, true);
        masterIncoming = Scope(options.MasterKey, false);
    }

    private bool IsAes => options.Suite == DlmsSecuritySuite.AesGcm128;

    /// <inheritdoc />
    public byte ApplicationContext => 3;

    /// <inheritdoc />
    public byte AuthenticationMechanism => IsAes ? (byte)5 : (byte)128;

    /// <inheritdoc />
    public ImmutableArray<byte> LocalSystemTitle => options.LocalSystemTitle;

    /// <inheritdoc />
    public ImmutableArray<byte> PeerSystemTitle => options.PeerSystemTitle;

    /// <inheritdoc />
    public ImmutableArray<byte> DedicatedKey => options.DedicatedKey;

    /// <inheritdoc />
    public byte[] CreateChallenge() => RandomNumberGenerator.GetBytes(ChallengeSize);

    /// <inheritdoc />
    public int GetProtectedLength(byte tag, int length, bool established)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(length);
        int contentLength = checked(HeaderSize + length + (Authenticates((byte)OutgoingMode(tag)) ? tagSize : 0));
        return checked(1 + (UsesGeneralTag(tag, established) ? 1 + TitleSize : 0)
            + LengthSize(contentLength) + contentLength);
    }

    /// <inheritdoc />
    public byte[] Protect(ReadOnlySpan<byte> apdu, bool established)
    {
        if (apdu.IsEmpty)
        {
            throw new ArgumentException("An xDLMS APDU is required.", nameof(apdu));
        }

        bool dedicated = established && !DedicatedKey.IsDefaultOrEmpty;
        bool general = UsesGeneralTag(apdu[0], established);
        byte tag = general ? (dedicated ? GeneralDedicated : GeneralGlobal) : CipheredTag(apdu[0], dedicated);
        byte control = (byte)((byte)options.Suite | (byte)OutgoingMode(apdu[0]));
        uint counter = Reserve(dedicated ? dedicatedOutgoing : globalOutgoing);
        var nonce = Nonce(LocalSystemTitle, counter);
        byte[] body = ProtectContent(control, nonce, Key(dedicated), apdu);

        var writer = new ArrayBufferWriter<byte>(GetProtectedLength(apdu[0], apdu.Length, established));
        WireWriter.Byte(writer, tag);
        if (general)
        {
            WireWriter.Length(writer, TitleSize);
            WireWriter.Bytes(writer, LocalSystemTitle.AsSpan());
        }

        WireWriter.Length(writer, checked(HeaderSize + body.Length));
        WireWriter.Byte(writer, control);
        WireWriter.U32(writer, counter);
        WireWriter.Bytes(writer, body);
        return writer.WrittenSpan.ToArray();
    }

    /// <inheritdoc />
    public byte[] Unprotect(ReadOnlySpan<byte> apdu, bool established)
    {
        try
        {
            return UnprotectCore(apdu, established);
        }
        catch (WireException)
        {
            throw new CryptographicException("The protected xDLMS envelope has invalid lengths or fields.");
        }
    }

    private byte[] UnprotectCore(ReadOnlySpan<byte> apdu, bool established)
    {
        var reader = new WireReader(apdu);
        byte tag = reader.Byte();
        bool general = tag is GeneralGlobal or GeneralDedicated;
        bool dedicated = tag == GeneralDedicated || IsDedicatedTag(tag);
        if (!general && !IsServiceCipheringTag(tag))
        {
            if (options.MinimumIncomingMode == ApduProtectionMode.None)
            {
                return apdu.ToArray();
            }

            throw new CryptographicException("The peer sent an unprotected xDLMS APDU.");
        }

        if (dedicated && (!established || DedicatedKey.IsDefaultOrEmpty))
        {
            throw new CryptographicException("Dedicated ciphering is unavailable in the current association state.");
        }

        if (general)
        {
            var title = reader.Take(reader.Length(TitleSize));
            if (!title.SequenceEqual(PeerSystemTitle.AsSpan()))
            {
                throw new CryptographicException("The protected xDLMS APDU has an unexpected system title.");
            }
        }

        if (reader.Length(apdu.Length) != reader.Remaining)
        {
            throw new CryptographicException("The protected xDLMS envelope length is invalid.");
        }

        byte control = reader.Byte();
        byte required = (byte)options.MinimumIncomingMode;
        if ((control & UnsupportedControlMask) != 0 || (control & SuiteMask) != (byte)options.Suite
            || (control & ProtectionMask) == 0 || (control & required) != required)
        {
            throw new CryptographicException("The peer's security control byte does not meet the configured policy.");
        }

        uint counter = reader.U32();
        if (counter == uint.MaxValue)
        {
            throw new CryptographicException("The peer's invocation counter is exhausted.");
        }

        var content = reader.Take(reader.Remaining);
        if (content.Length <= (Authenticates(control) ? tagSize : 0))
        {
            throw new CryptographicException("The protected xDLMS content is truncated.");
        }

        byte[] clear = UnprotectContent(control, Nonce(PeerSystemTitle, counter), Key(dedicated), content);
        try
        {
            if (!general && CipheredTag(clear[0], dedicated) != tag)
            {
                throw new CryptographicException("The ciphered tag does not match the inner xDLMS service.");
            }

            Accept(dedicated ? dedicatedIncoming : globalIncoming, counter);
            return clear;
        }
        catch
        {
            CryptographicOperations.ZeroMemory(clear);
            throw;
        }
    }

    /// <inheritdoc />
    public byte[] CreateReply(ReadOnlySpan<byte> localChallenge, ReadOnlySpan<byte> peerChallenge)
    {
        ValidateChallenges(localChallenge, peerChallenge);
        uint counter = Reserve(globalOutgoing);
        byte control = (byte)((byte)ApduProtectionMode.Authentication | (byte)options.Suite);
        byte[] tag = ChallengeTag(LocalSystemTitle, counter, control, peerChallenge, localChallenge);
        byte[] reply = new byte[HeaderSize + tag.Length];
        reply[0] = control;
        BinaryPrimitives.WriteUInt32BigEndian(reply.AsSpan(1), counter);
        tag.CopyTo(reply.AsSpan(HeaderSize));
        return reply;
    }

    /// <inheritdoc />
    public void VerifyReply(ReadOnlySpan<byte> localChallenge, ReadOnlySpan<byte> peerChallenge, ReadOnlySpan<byte> reply)
    {
        ValidateChallenges(localChallenge, peerChallenge);
        byte control = (byte)((byte)ApduProtectionMode.Authentication | (byte)options.Suite);
        if (reply.Length != HeaderSize + tagSize || reply[0] != control)
        {
            throw new CryptographicException("The peer returned an invalid HLS reply.");
        }

        uint counter = BinaryPrimitives.ReadUInt32BigEndian(reply[1..]);
        byte[] expected = ChallengeTag(PeerSystemTitle, counter, control, localChallenge, peerChallenge);
        if (!CryptographicOperations.FixedTimeEquals(expected, reply[HeaderSize..]))
        {
            throw new CryptographicException("The peer's HLS reply could not be authenticated.");
        }

        Accept(hlsIncoming, counter);
    }

    /// <summary>Wraps a symmetric key for Security Setup key_transfer using the configured master key.</summary>
    /// <param name="key">A key of the selected suite's prescribed length.</param>
    public byte[] WrapKey(ReadOnlySpan<byte> key)
    {
        RequireMasterKey();
        if (key.Length != keySize)
        {
            throw new ArgumentException("Invalid key length for this suite.", nameof(key));
        }

        if (IsAes)
        {
            return AesDlmsCipher.Wrap(true, options.MasterKey.AsSpan(), key);
        }

        uint counter = Reserve(masterOutgoing);
        byte[] wrapped = KuznyechikDlmsCipher.Wrap(options.MasterKey.AsSpan(), Nonce(LocalSystemTitle, counter), key);
        byte[] result = new byte[sizeof(uint) + wrapped.Length];
        BinaryPrimitives.WriteUInt32BigEndian(result, counter);
        wrapped.CopyTo(result.AsSpan(sizeof(uint)));
        return result;
    }

    /// <summary>Unwraps a peer's symmetric key after validating integrity and, for Kuznyechik, its counter.</summary>
    /// <param name="wrapped">The wrapped key octets from Security Setup key_transfer.</param>
    public byte[] UnwrapKey(ReadOnlySpan<byte> wrapped)
    {
        RequireMasterKey();
        int expectedLength = keySize + (IsAes ? 8 : sizeof(uint) + KuznyechikDlmsCipher.WrapTagSize);
        if (wrapped.Length != expectedLength)
        {
            throw new CryptographicException("The wrapped key length is invalid.");
        }

        if (IsAes)
        {
            return AesDlmsCipher.Wrap(false, options.MasterKey.AsSpan(), wrapped);
        }

        uint counter = BinaryPrimitives.ReadUInt32BigEndian(wrapped);
        byte[] key = KuznyechikDlmsCipher.Unwrap(options.MasterKey.AsSpan(), Nonce(PeerSystemTitle, counter), wrapped[sizeof(uint)..]);
        try
        {
            Accept(masterIncoming, counter);
            return key;
        }
        catch
        {
            CryptographicOperations.ZeroMemory(key);
            throw;
        }
    }

    private byte[] ProtectContent(byte control, ReadOnlySpan<byte> nonce, ReadOnlySpan<byte> key, ReadOnlySpan<byte> clear)
    {
        bool authenticated = Authenticates(control);
        bool encrypted = Encrypts(control);
        if (IsAes && encrypted && authenticated)
        {
            return AesDlmsCipher.Gcm(true, key, nonce, AesAssociatedData(control), clear);
        }

        byte[] content = encrypted ? Transform(key, nonce, clear) : clear.ToArray();
        return authenticated ? CipherModes.Join(content, AuthenticationTag(control, nonce, key, content)) : content;
    }

    private byte[] UnprotectContent(byte control, ReadOnlySpan<byte> nonce, ReadOnlySpan<byte> key, ReadOnlySpan<byte> content)
    {
        bool authenticated = Authenticates(control);
        bool encrypted = Encrypts(control);
        if (IsAes && encrypted && authenticated)
        {
            return AesDlmsCipher.Gcm(false, key, nonce, AesAssociatedData(control), content);
        }

        int length = content.Length - (authenticated ? tagSize : 0);
        ReadOnlySpan<byte> data = content[..length];
        if (authenticated && !CryptographicOperations.FixedTimeEquals(AuthenticationTag(control, nonce, key, data), content[length..]))
        {
            throw new CryptographicException("The DLMS authentication tag is invalid.");
        }

        return encrypted ? Transform(key, nonce, data) : data.ToArray();
    }

    private byte[] ChallengeTag(ImmutableArray<byte> sender, uint counter, byte control,
        ReadOnlySpan<byte> challenge, ReadOnlySpan<byte> otherChallenge)
    {
        byte[] nonce = Nonce(sender, counter);
        ReadOnlySpan<byte> data = IsAes ? challenge : CipherModes.Join(challenge, otherChallenge);
        return AuthenticationTag(control, nonce, options.GlobalKey.AsSpan(), data);
    }

    private byte[] AuthenticationTag(byte control, ReadOnlySpan<byte> nonce, ReadOnlySpan<byte> key, ReadOnlySpan<byte> data)
        => IsAes
            ? AesDlmsCipher.Gcm(true, key, nonce, AesAssociatedData(control, data), [])
            : KuznyechikDlmsCipher.Mac(key, CipherModes.Join(nonce, [control], data));

    private byte[] AesAssociatedData(byte control, ReadOnlySpan<byte> clear = default)
        => CipherModes.Join([control], options.AuthenticationKey.AsSpan(), clear);

    private byte[] Transform(ReadOnlySpan<byte> key, ReadOnlySpan<byte> nonce, ReadOnlySpan<byte> data)
        => IsAes ? AesDlmsCipher.Transform(key, nonce, data) : KuznyechikDlmsCipher.Transform(key, nonce, data);

    private ReadOnlySpan<byte> Key(bool dedicated)
        => dedicated ? DedicatedKey.AsSpan() : options.GlobalKey.AsSpan();

    private string Scope(ImmutableArray<byte> key, bool outgoing)
    {
        if (key.IsDefaultOrEmpty)
        {
            return string.Empty;
        }

        // AES-GCM requires nonce uniqueness for the encryption key regardless of the authentication key.
        ReadOnlySpan<byte> encryptionKey = IsAes ? key.AsSpan() : key.AsSpan()[..KuznyechikEngine.KeySize];
        string fingerprint = Convert.ToHexString(SHA256.HashData(encryptionKey));
        return outgoing
            ? $"tx:{(byte)options.Suite}:{Convert.ToHexString(LocalSystemTitle.AsSpan())}:{fingerprint}"
            : $"rx:{(byte)options.Suite}:{Convert.ToHexString(PeerSystemTitle.AsSpan())}:{Convert.ToHexString(LocalSystemTitle.AsSpan())}:{fingerprint}";
    }

    private uint Reserve(string scope)
    {
        uint counter = options.Counters.Reserve(scope);
        return counter < uint.MaxValue ? counter : throw new CryptographicException("The invocation counter is exhausted.");
    }

    private void Accept(string scope, uint counter)
    {
        if (counter == uint.MaxValue || !options.Counters.TryAccept(scope, counter))
        {
            throw new CryptographicException("The peer's invocation counter is a replay or is exhausted.");
        }
    }

    private void RequireMasterKey()
    {
        if (options.MasterKey.IsDefaultOrEmpty)
        {
            throw new InvalidOperationException("Key transfer requires a master key.");
        }
    }

    private static byte[] Nonce(ImmutableArray<byte> title, uint counter)
    {
        byte[] nonce = new byte[TitleSize + sizeof(uint)];
        title.AsSpan().CopyTo(nonce);
        BinaryPrimitives.WriteUInt32BigEndian(nonce.AsSpan(TitleSize), counter);
        return nonce;
    }

    private static void ValidateChallenges(ReadOnlySpan<byte> local, ReadOnlySpan<byte> peer)
    {
        if (local.Length is < MinChallengeSize or > MaxChallengeSize
            || peer.Length is < MinChallengeSize or > MaxChallengeSize)
        {
            throw new CryptographicException("The HLS challenge length is invalid.");
        }
    }

    private bool UsesGeneralTag(byte tag, bool established)
        => !HasServiceCiphering(tag) || (established && options.UseGeneralCiphering);

    private ApduProtectionMode OutgoingMode(byte tag)
        => !DedicatedKey.IsDefaultOrEmpty && tag == (byte)ApduTag.InitiateRequest
            ? ApduProtectionMode.AuthenticationAndEncryption : options.OutgoingMode;

    private static bool Authenticates(byte control) => (control & (byte)ApduProtectionMode.Authentication) != 0;
    private static bool Encrypts(byte control) => (control & (byte)ApduProtectionMode.Encryption) != 0;
    private static int LengthSize(int length) => length switch
    {
        < 0x80 => 1,
        <= byte.MaxValue => 2,
        <= ushort.MaxValue => 3,
        <= 0xffffff => 4,
        _ => 5
    };

    private static bool HasServiceCiphering(byte tag) => tag is (byte)ApduTag.InitiateRequest or (byte)ApduTag.InitiateResponse
        or (byte)ApduTag.GetRequest or (byte)ApduTag.SetRequest or 0xc2 or (byte)ApduTag.ActionRequest
        or (byte)ApduTag.GetResponse or (byte)ApduTag.SetResponse or (byte)ApduTag.ActionResponse;

    private static bool IsDedicatedTag(byte tag) => tag is 0x41 or 0x48 or 0xd0 or 0xd1 or 0xd2 or 0xd3 or 0xd4 or 0xd5 or 0xd7;
    private static bool IsServiceCipheringTag(byte tag) => IsDedicatedTag(tag)
        || tag is 0x21 or 0x28 or 0xc8 or 0xc9 or 0xca or 0xcb or 0xcc or 0xcd or 0xcf;

    private static byte CipheredTag(byte tag, bool dedicated)
    {
        if (!HasServiceCiphering(tag))
        {
            throw new CryptographicException("No service-specific ciphering tag exists for the xDLMS APDU.");
        }

        int offset = tag is (byte)ApduTag.InitiateRequest or (byte)ApduTag.InitiateResponse
            ? dedicated ? 0x40 : 0x20
            : dedicated ? 0x10 : 0x08;
        return (byte)(tag + offset);
    }
}
