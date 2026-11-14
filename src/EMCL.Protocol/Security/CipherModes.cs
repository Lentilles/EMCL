using System.Buffers.Binary;
using System.Security.Cryptography;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Parameters;

namespace EMCL.Protocol.Security;

internal static class CipherModes
{
    internal const int BlockSize = 16;

    internal static byte[] Transform(IBlockCipher cipher, ReadOnlySpan<byte> key,
        ReadOnlySpan<byte> nonce, uint initialCounter, ReadOnlySpan<byte> input)
    {
        cipher.Init(true, new KeyParameter(key));
        Span<byte> counter = stackalloc byte[BlockSize];
        counter.Clear();
        nonce.CopyTo(counter);
        Span<byte> gamma = stackalloc byte[BlockSize];
        var result = new byte[input.Length];
        try
        {
            for (int offset = 0; offset < input.Length;)
            {
                BinaryPrimitives.WriteUInt32BigEndian(counter[^sizeof(uint)..], initialCounter++);
                cipher.ProcessBlock(counter, gamma);
                int count = Math.Min(BlockSize, input.Length - offset);
                for (int i = 0; i < count; i++)
                {
                    result[offset + i] = (byte)(input[offset + i] ^ gamma[i]);
                }

                offset += count;
            }

            return result;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(gamma);
        }
    }

    internal static byte[] Join(ReadOnlySpan<byte> first, ReadOnlySpan<byte> second, ReadOnlySpan<byte> third = default)
    {
        byte[] result = new byte[checked(first.Length + second.Length + third.Length)];
        first.CopyTo(result);
        second.CopyTo(result.AsSpan(first.Length));
        third.CopyTo(result.AsSpan(first.Length + second.Length));
        return result;
    }
}
