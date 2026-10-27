namespace EMCL.Core.Objects;

/// <summary>Represents a six-octet COSEM logical name, conventionally displayed as six decimal OBIS groups.</summary>
/// <param name="Value">The unsigned 48-bit logical-name value in network-octet order.</param>
public readonly record struct LogicalName(ulong Value)
{
    /// <summary>The encoded logical-name length in octets.</summary>
    public const int EncodedLength = 6;
    private const ulong MaximumValue = (1UL << (EncodedLength * 8)) - 1;

    /// <summary>Decodes exactly six logical-name octets in network order.</summary>
    /// <param name="bytes">The bytes to copy or decode.</param>
    /// <returns>The decoded six-octet logical name.</returns>
    /// <exception cref="ArgumentException">The input does not contain exactly six octets.</exception>
    public static LogicalName FromBytes(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length != EncodedLength)
        {
            throw new ArgumentException("Logical name has six octets.");
        }

        ulong value = 0;
        foreach (byte b in bytes)
        {
            value = (value << 8) | b;
        }

        return new(value);
    }

    /// <summary>Returns the logical name as a new six-octet array in network order.</summary>
    /// <returns>A new six-octet logical-name array.</returns>
    /// <exception cref="InvalidOperationException">The stored value exceeds 48 bits.</exception>
    public byte[] ToBytes()
    {
        if (Value > MaximumValue)
        {
            throw new InvalidOperationException("Logical name exceeds six octets.");
        }

        var result = new byte[EncodedLength];
        ulong v = Value;
        for (int i = EncodedLength - 1; i >= 0; i--)
        {
            result[i] = (byte)v;
            v >>= 8;
        }

        return result;
    }

    /// <summary>Parses six dot-separated decimal octets into a logical name.</summary>
    /// <param name="text">The six dot-separated decimal logical-name groups.</param>
    /// <returns>The parsed logical name.</returns>
    /// <exception cref="FormatException">A group is not a decimal octet.</exception>
    /// <exception cref="OverflowException">A group is outside the octet range.</exception>
    /// <exception cref="ArgumentException">The logical name does not have six groups.</exception>
    public static LogicalName Parse(string text) => FromBytes(text.Split('.').Select(part => byte.Parse(part, System.Globalization.CultureInfo.InvariantCulture)).ToArray());
    /// <summary>Formats the logical name as six dot-separated decimal octets.</summary>
    /// <returns>The six dot-separated decimal groups.</returns>
    public override string ToString() => string.Join('.', ToBytes());
}
