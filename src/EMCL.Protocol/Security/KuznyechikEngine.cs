using System.Security.Cryptography;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Parameters;

namespace EMCL.Protocol.Security;

// Forward block transform from GOST R 34.12-2015 (RFC 7801, sections 4.1–4.5).
// CTR and CMAC only require the forward transform.
internal sealed class KuznyechikEngine : IBlockCipher, IDisposable
{
    internal const int KeySize = 32;
    private const int BlockSize = CipherModes.BlockSize;
    private const int RoundCount = 10;

    private static ReadOnlySpan<byte> Substitution =>
    [
        252, 238, 221, 17, 207, 110, 49, 22, 251, 196, 250, 218, 35, 197, 4, 77,
        233, 119, 240, 219, 147, 46, 153, 186, 23, 54, 241, 187, 20, 205, 95, 193,
        249, 24, 101, 90, 226, 92, 239, 33, 129, 28, 60, 66, 139, 1, 142, 79,
        5, 132, 2, 174, 227, 106, 143, 160, 6, 11, 237, 152, 127, 212, 211, 31,
        235, 52, 44, 81, 234, 200, 72, 171, 242, 42, 104, 162, 253, 58, 206, 204,
        181, 112, 14, 86, 8, 12, 118, 18, 191, 114, 19, 71, 156, 183, 93, 135,
        21, 161, 150, 41, 16, 123, 154, 199, 243, 145, 120, 111, 157, 158, 178, 177,
        50, 117, 25, 61, 255, 53, 138, 126, 109, 84, 198, 128, 195, 189, 13, 87,
        223, 245, 36, 169, 62, 168, 67, 201, 215, 121, 214, 246, 124, 34, 185, 3,
        224, 15, 236, 222, 122, 148, 176, 188, 220, 232, 40, 80, 78, 51, 10, 74,
        167, 151, 96, 115, 30, 0, 98, 68, 26, 184, 56, 130, 100, 159, 38, 65,
        173, 69, 70, 146, 39, 94, 85, 47, 140, 163, 165, 125, 105, 213, 149, 59,
        7, 88, 179, 64, 134, 172, 29, 247, 48, 55, 107, 228, 136, 217, 231, 137,
        225, 27, 131, 73, 76, 63, 248, 254, 141, 83, 170, 144, 202, 216, 133, 97,
        32, 113, 103, 164, 45, 43, 9, 91, 203, 155, 37, 208, 190, 229, 108, 82,
        89, 166, 116, 210, 230, 244, 180, 192, 209, 102, 175, 194, 57, 75, 99, 182
    ];

    private static ReadOnlySpan<byte> LinearCoefficients => [148, 32, 133, 16, 194, 192, 1, 251, 1, 192, 194, 16, 133, 32, 148, 1];
    private static readonly byte[][] RoundConstants = CreateRoundConstants();
    private readonly byte[] roundKeys = new byte[RoundCount * BlockSize];

    public string AlgorithmName => "Kuznyechik";
    public int GetBlockSize() => BlockSize;

    public void Init(bool forEncryption, ICipherParameters parameters)
    {
        if (!forEncryption || parameters is not KeyParameter parameter || parameter.KeyLength != KeySize)
        {
            throw new ArgumentException("A 256-bit Kuznyechik encryption key is required.", nameof(parameters));
        }

        byte[] key = parameter.GetKey();
        try
        {
            SetKey(key);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    private void SetKey(ReadOnlySpan<byte> key)
    {
        Span<byte> left = stackalloc byte[BlockSize];
        Span<byte> right = stackalloc byte[BlockSize];
        Span<byte> transformed = stackalloc byte[BlockSize];
        key.CopyTo(roundKeys);
        key[..BlockSize].CopyTo(left);
        key[BlockSize..].CopyTo(right);
        for (int i = 0; i < RoundConstants.Length; i++)
        {
            left.CopyTo(transformed);
            Round(transformed, RoundConstants[i]);
            Xor(transformed, right);
            left.CopyTo(right);
            transformed.CopyTo(left);
            if ((i + 1) % 8 == 0)
            {
                int offset = (1 + (i + 1) / 8) * 2 * BlockSize - 2 * BlockSize;
                left.CopyTo(roundKeys.AsSpan(offset));
                right.CopyTo(roundKeys.AsSpan(offset + BlockSize));
            }
        }

        CryptographicOperations.ZeroMemory(left);
        CryptographicOperations.ZeroMemory(right);
        CryptographicOperations.ZeroMemory(transformed);
    }

    public int ProcessBlock(byte[] input, int inOff, byte[] output, int outOff)
        => ProcessBlock(input.AsSpan(inOff), output.AsSpan(outOff));

    public int ProcessBlock(ReadOnlySpan<byte> input, Span<byte> output)
    {
        Span<byte> state = stackalloc byte[BlockSize];
        input[..BlockSize].CopyTo(state);
        for (int round = 0; round < RoundCount - 1; round++)
        {
            Round(state, roundKeys.AsSpan(round * BlockSize, BlockSize));
        }

        Xor(state, roundKeys.AsSpan((RoundCount - 1) * BlockSize, BlockSize));
        state.CopyTo(output);
        CryptographicOperations.ZeroMemory(state);
        return BlockSize;
    }

    private static void Round(Span<byte> state, ReadOnlySpan<byte> key)
    {
        for (int i = 0; i < state.Length; i++)
        {
            int index = state[i] ^ key[i];
            int selected = 0;
            for (int candidate = 0; candidate < Substitution.Length; candidate++)
            {
                // Select the substitution without indexing the table with secret data.
                int mask = ((index ^ candidate) - 1) >> 31;
                selected |= Substitution[candidate] & mask;
            }

            state[i] = (byte)selected;
        }

        Linear(state);
    }

    private static void Xor(Span<byte> block, ReadOnlySpan<byte> mask)
    {
        for (int i = 0; i < block.Length; i++)
        {
            block[i] ^= mask[i];
        }
    }

    private static void Linear(Span<byte> block)
    {
        for (int round = 0; round < BlockSize; round++)
        {
            byte first = 0;
            for (int i = 0; i < BlockSize; i++)
            {
                first ^= Multiply(block[i], LinearCoefficients[i]);
            }

            block[..^1].CopyTo(block[1..]);
            block[0] = first;
        }
    }

    private static byte Multiply(int value, int coefficient)
    {
        int result = 0;
        for (int bit = 0; bit < 8; bit++)
        {
            result ^= value & -(coefficient & 1);
            value = ((value << 1) ^ (0x1c3 & -(value >> 7))) & byte.MaxValue;
            coefficient >>= 1;
        }

        return (byte)result;
    }

    private static byte[][] CreateRoundConstants()
    {
        byte[][] constants = new byte[32][];
        for (int i = 0; i < constants.Length; i++)
        {
            var block = new byte[BlockSize];
            block[^1] = (byte)(i + 1);
            Linear(block);
            constants[i] = block;
        }

        return constants;
    }

    public void Dispose() => CryptographicOperations.ZeroMemory(roundKeys);
}
