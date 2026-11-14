using System.Security.Cryptography;
using Org.BouncyCastle.Crypto.Macs;
using Org.BouncyCastle.Crypto.Parameters;

namespace EMCL.Protocol.Security;

internal static class KuznyechikDlmsCipher
{
    internal const int KeySize = 2 * KuznyechikEngine.KeySize;
    internal const int TagSize = 12;
    internal const int WrapTagSize = CipherModes.BlockSize;

    internal static byte[] Transform(ReadOnlySpan<byte> key, ReadOnlySpan<byte> nonce, ReadOnlySpan<byte> input)
    {
        using var engine = new KuznyechikEngine();
        return CipherModes.Transform(engine, key[..KuznyechikEngine.KeySize], nonce, 0, input);
    }

    internal static byte[] Mac(ReadOnlySpan<byte> key, ReadOnlySpan<byte> data, int size = TagSize)
    {
        using var engine = new KuznyechikEngine();
        var mac = new CMac(engine, size * 8);
        mac.Init(new KeyParameter(key[KuznyechikEngine.KeySize..]));
        mac.BlockUpdate(data);
        byte[] output = new byte[size];
        mac.DoFinal(output);
        return output;
    }

    internal static byte[] Wrap(ReadOnlySpan<byte> key, ReadOnlySpan<byte> nonce, ReadOnlySpan<byte> material)
    {
        var authenticated = CipherModes.Join(nonce, material);
        byte[]? clear = null;
        try
        {
            clear = CipherModes.Join(material, Mac(key, authenticated, WrapTagSize));
            return Transform(key, nonce, clear);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(authenticated);
            if (clear is not null)
            {
                CryptographicOperations.ZeroMemory(clear);
            }
        }
    }

    internal static byte[] Unwrap(ReadOnlySpan<byte> key, ReadOnlySpan<byte> nonce, ReadOnlySpan<byte> wrapped)
    {
        byte[] clear = Transform(key, nonce, wrapped);
        var material = clear.AsSpan(..^WrapTagSize);
        var authenticated = CipherModes.Join(nonce, material);
        try
        {
            if (!CryptographicOperations.FixedTimeEquals(Mac(key, authenticated, WrapTagSize), clear.AsSpan(^WrapTagSize..)))
            {
                throw new CryptographicException("The wrapped key failed its integrity check.");
            }

            return material.ToArray();
        }
        finally
        {
            CryptographicOperations.ZeroMemory(authenticated);
            CryptographicOperations.ZeroMemory(clear);
        }
    }
}
