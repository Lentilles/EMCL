using System.Collections.Immutable;

namespace EMCL.Core.Values;

/// <summary>Stores a bit count and its packed, most-significant-bit-first octets.</summary>
public sealed record DlmsBitString
{
    /// <summary>Gets the number of significant bits, excluding padding in the last octet.</summary>
    public int BitCount
    {
        get;
    }
    /// <summary>Gets the packed octets, with significant bits starting at the most significant bit.</summary>
    public ImmutableArray<byte> Bytes
    {
        get;
    }

    /// <summary>Creates a bit string after checking that its octet count matches the declared bit count.</summary>
    /// <param name="bitCount">The number of significant bits.</param>
    /// <param name="bytes">The bytes to copy or decode.</param>
    public DlmsBitString(int bitCount, ReadOnlySpan<byte> bytes)
    {
        if (bitCount < 0 || bytes.Length != (bitCount + 7L) / 8)
        {
            throw new ArgumentException("Invalid bit count.");
        }

        BitCount = bitCount;
        Bytes = bytes.ToArray().ToImmutableArray();
    }
}
