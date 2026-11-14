using System.Security.Cryptography;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Engines;
using Org.BouncyCastle.Crypto.Modes;
using Org.BouncyCastle.Crypto.Parameters;

namespace EMCL.Protocol.Security;

internal static class AesDlmsCipher
{
    internal const int KeySize = 16;
    internal const int TagSize = 12;

    internal static byte[] Gcm(bool encrypt, ReadOnlySpan<byte> key, ReadOnlySpan<byte> nonce,
        ReadOnlySpan<byte> associatedData, ReadOnlySpan<byte> input)
    {
        var cipher = new GcmBlockCipher(new AesEngine());
        cipher.Init(encrypt, new AeadParameters(new KeyParameter(key), TagSize * 8,
            nonce.ToArray(), associatedData.ToArray()));
        byte[] output = new byte[cipher.GetOutputSize(input.Length)];
        try
        {
            int written = cipher.ProcessBytes(input, output);
            cipher.DoFinal(output.AsSpan(written));
            return output;
        }
        catch (InvalidCipherTextException)
        {
            CryptographicOperations.ZeroMemory(output);
            throw new CryptographicException("The DLMS authentication tag is invalid.");
        }
    }

    // GCM encryption without authentication uses counter mode starting at inc32(J0).
    internal static byte[] Transform(ReadOnlySpan<byte> key, ReadOnlySpan<byte> nonce, ReadOnlySpan<byte> input)
        => CipherModes.Transform(new AesEngine(), key, nonce, 2, input);

    internal static byte[] Wrap(bool wrap, ReadOnlySpan<byte> key, ReadOnlySpan<byte> input)
    {
        var engine = new AesWrapEngine();
        engine.Init(wrap, new KeyParameter(key));
        byte[] bytes = input.ToArray();
        try
        {
            return wrap ? engine.Wrap(bytes, 0, bytes.Length) : engine.Unwrap(bytes, 0, bytes.Length);
        }
        catch (InvalidCipherTextException)
        {
            throw new CryptographicException("The wrapped key failed its integrity check.");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }
}
