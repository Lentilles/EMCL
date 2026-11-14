using System.Collections.Immutable;
using System.Security.Cryptography;
using EMCL.Protocol.Security;

namespace EMCL.Tests.Protocol.Security;

public sealed class DlmsSecurityProtocolTests
{
    internal static readonly ImmutableArray<byte> ClientTitle = Bytes("4D4D4D0000000001");
    internal static readonly ImmutableArray<byte> ServerTitle = Bytes("4D4D4D0000BC614E");
    internal static readonly ImmutableArray<byte> AesKey = Bytes("000102030405060708090A0B0C0D0E0F");

    [Fact]
    public void GmacMatchesGreenBookTable51()
    {
        var client = Create(DlmsSecuritySuite.AesGcm128, initialCounter: 1);
        var server = Create(DlmsSecuritySuite.AesGcm128, server: true, initialCounter: 0x01234567);
        byte[] clientChallenge = Convert.FromHexString("4B35366956616759");
        byte[] serverChallenge = Convert.FromHexString("503677524A323146");
        byte[] clientReply = client.CreateReply(clientChallenge, serverChallenge);
        byte[] serverReply = server.CreateReply(serverChallenge, clientChallenge);
        Assert.Equal(Convert.FromHexString("10000000011A52FE7DD3E72748973C1E28"), clientReply);
        Assert.Equal(Convert.FromHexString("1001234567FE1466AFB3DBCD4F9389E2B7"), serverReply);
        client.VerifyReply(clientChallenge, serverChallenge, serverReply);
        server.VerifyReply(serverChallenge, clientChallenge, clientReply);
        Assert.Throws<CryptographicException>(() => client.VerifyReply(clientChallenge, serverChallenge, serverReply));
    }

    [Fact]
    public void AesKeyWrapMatchesRfc3394()
    {
        var client = Create(DlmsSecuritySuite.AesGcm128);
        var server = Create(DlmsSecuritySuite.AesGcm128, server: true);
        byte[] key = Convert.FromHexString("00112233445566778899AABBCCDDEEFF");
        byte[] wrapped = client.WrapKey(key);
        Assert.Equal(Convert.FromHexString("1FA68B0A8112B447AEF34BD8FB5A7B829D3E862371D2CFE5"), wrapped);
        Assert.Equal(key, server.UnwrapKey(wrapped));
        wrapped[^1] ^= 1;
        Assert.Throws<CryptographicException>(() => server.UnwrapKey(wrapped));
    }

    [Fact]
    public void KuznyechikMatchesTheRfc7801BlockCipherVector()
    {
        var cipher = new KuznyechikEngine();
        using (cipher)
        {
            cipher.Init(true, new Org.BouncyCastle.Crypto.Parameters.KeyParameter(
                Convert.FromHexString("8899AABBCCDDEEFF0011223344556677FEDCBA98765432100123456789ABCDEF")));
            byte[] output = new byte[16];
            cipher.ProcessBlock(Convert.FromHexString("1122334455667700FFEEDDCCBBAA9988"), output);
            Assert.Equal(Convert.FromHexString("7F679D90BEBC24305A468D42B9D4EDCD"), output);
        }
    }

    [Fact]
    public void KuznyechikCtrAndCmacMatchGost3413Examples()
    {
        byte[] key = Convert.FromHexString("8899AABBCCDDEEFF0011223344556677FEDCBA98765432100123456789ABCDEF");
        byte[] pair = [.. key, .. key];
        byte[] input = Convert.FromHexString("1122334455667700FFEEDDCCBBAA998800112233445566778899AABBCCEEFF0A"
            + "112233445566778899AABBCCEEFF0A002233445566778899AABBCCEEFF0A0011");
        byte[] iv = Convert.FromHexString("1234567890ABCEF0");
        Assert.Equal(Convert.FromHexString("F195D8BEC10ED1DBD57B5FA240BDA1B885EEE733F6A13E5DF33CE4B33C45DEE4"
            + "A5EAE88BE6356ED3D5E877F13564A3A5CB91FAB1F20CBAB6D1C6D15820BDBA73"),
            KuznyechikDlmsCipher.Transform(pair, iv, input));
        Assert.Equal(Convert.FromHexString("336F4D296059FBE34DDEB35B37749C67"),
            KuznyechikDlmsCipher.Mac(pair, input, 16));
    }

    [Fact]
    public void KuznyechikKeyExportMatchesRfc9189()
    {
        byte[] key = Convert.FromHexString("1F1CBAD8866166F01FFAAB0152E24BF4609D5F46A5C899C787900D08B9FCAD24"
            + "7DAC56E48A4DC170FAA8FCBAE20DB845450CCCC4C6328BDC8D01157CEFA2A5F1");
        byte[] iv = Convert.FromHexString("214A6A298E99E325");
        byte[] secret = Convert.FromHexString("A5576CE7924A24F58113808DBD9EF856F5BDC3B183CE5DADCA36A53AA077651D");
        byte[] exported = Convert.FromHexString("250D1B67A270AB04D3F65418E1D380B4CB945F0A3DCA51500CF3A1BEF37F76C0"
            + "7341A9839CCF6CBA7189DA61EB67176C");
        Assert.Equal(exported, KuznyechikDlmsCipher.Wrap(key, iv, secret));
        Assert.Equal(secret, KuznyechikDlmsCipher.Unwrap(key, iv, exported));
    }

    [Theory]
    [InlineData(DlmsSecuritySuite.AesGcm128)]
    [InlineData(DlmsSecuritySuite.Kuznyechik)]
    public void RejectsTamperingDowngradeAndReplay(DlmsSecuritySuite suite)
    {
        var client = Create(suite);
        var server = Create(suite, server: true);
        byte[] apdu = Convert.FromHexString("C0014100010000600100FF0200");
        byte[] secure = client.Protect(apdu, true);
        Assert.Equal(client.GetProtectedLength(apdu[0], apdu.Length, true), secure.Length);
        Assert.Equal(0xc8, secure[0]);
        var tampered = secure.ToArray();
        tampered[^1] ^= 1;
        Assert.Throws<CryptographicException>(() => server.Unprotect(tampered, true));
        Assert.Throws<CryptographicException>(() => server.Unprotect(apdu, true));
        Assert.Throws<CryptographicException>(() => server.Unprotect([.. secure, 0], true));
        Assert.Equal(apdu, server.Unprotect(secure, true));
        Assert.Throws<CryptographicException>(() => server.Unprotect(secure, true));
    }

    [Theory]
    [InlineData(DlmsSecuritySuite.AesGcm128, ApduProtectionMode.Authentication)]
    [InlineData(DlmsSecuritySuite.AesGcm128, ApduProtectionMode.Encryption)]
    [InlineData(DlmsSecuritySuite.Kuznyechik, ApduProtectionMode.Authentication)]
    [InlineData(DlmsSecuritySuite.Kuznyechik, ApduProtectionMode.Encryption)]
    public void AppliesTheConfiguredProtectionAndGeneralDedicatedEnvelope(DlmsSecuritySuite suite, ApduProtectionMode mode)
    {
        var client = Create(suite, dedicated: true, mode: mode);
        var server = Create(suite, server: true, dedicated: true, mode: mode);
        byte[] apdu = [0x0f, .. new byte[120]];
        byte[] secure = client.Protect(apdu, true);
        Assert.Equal(0xdc, secure[0]);
        Assert.Equal(client.GetProtectedLength(apdu[0], apdu.Length, true), secure.Length);
        Assert.Throws<CryptographicException>(() => server.Unprotect(secure, false));
        Assert.Equal(apdu, server.Unprotect(secure, true));
    }

    [Fact]
    public void KuznyechikWrapRejectsReplayAndTampering()
    {
        var client = Create(DlmsSecuritySuite.Kuznyechik);
        var server = Create(DlmsSecuritySuite.Kuznyechik, server: true);
        byte[] key = Enumerable.Range(0, 64).Select(value => (byte)value).ToArray();
        byte[] wrapped = client.WrapKey(key);
        Assert.Equal(84, wrapped.Length);
        var tampered = wrapped.ToArray();
        tampered[^1] ^= 1;
        Assert.Throws<CryptographicException>(() => server.UnwrapKey(tampered));
        Assert.Equal(key, server.UnwrapKey(wrapped));
        Assert.Throws<CryptographicException>(() => server.UnwrapKey(wrapped));
    }

    [Fact]
    public void RefusesToReuseAnExhaustedCounter()
    {
        var client = Create(DlmsSecuritySuite.AesGcm128, initialCounter: uint.MaxValue - 1);
        Assert.Equal("FFFFFFFE", Convert.ToHexString(client.Protect([0xc0], false).AsSpan(3, 4)));
        Assert.Throws<CryptographicException>(() => client.Protect([0xc0], false));
    }

    internal static DlmsSecurityProtocol Create(DlmsSecuritySuite suite, bool server = false, uint initialCounter = 0,
        bool dedicated = false, ApduProtectionMode mode = ApduProtectionMode.AuthenticationAndEncryption)
    {
        bool aes = suite == DlmsSecuritySuite.AesGcm128;
        return new(new DlmsSecurityOptions
        {
            Suite = suite,
            LocalSystemTitle = server ? ServerTitle : ClientTitle,
            PeerSystemTitle = server ? ClientTitle : ServerTitle,
            GlobalKey = aes ? AesKey : Key(0, 64),
            AuthenticationKey = aes ? Bytes("D0D1D2D3D4D5D6D7D8D9DADBDCDDDEDF") : [],
            DedicatedKey = dedicated ? Key(128, aes ? 16 : 64) : [],
            MasterKey = aes ? AesKey : Key(64, 64),
            Counters = new MemoryInvocationCounterStore(initialCounter),
            OutgoingMode = mode,
            MinimumIncomingMode = mode
        });
    }

    private static ImmutableArray<byte> Bytes(string hex) => [.. Convert.FromHexString(hex)];
    private static ImmutableArray<byte> Key(int start, int count)
        => Enumerable.Range(start, count).Select(value => (byte)value).ToImmutableArray();
}
