namespace EMCL.Framing.Hdlc.Frames;

/// <summary>Computes the reflected CRC-16/X-25 checksum used for DLMS HDLC header and frame checks.</summary>
public static class HdlcChecksum
{
    private const ushort InitialValue = 0xffff;
    private const ushort ReflectedPolynomial = 0x8408;
    private const int BitsPerOctet = 8;

    /// <summary>Computes CRC-16/X-25 over the supplied octets, returning the complemented checksum before little-endian wire encoding.</summary>
    /// <param name="data">The octets included in the checksum calculation.</param>
    /// <returns>The final checksum value; transmit its least significant octet first.</returns>
    public static ushort Compute(ReadOnlySpan<byte> data)
    {
        ushort crc = InitialValue;
        foreach (byte value in data)
        {
            crc ^= value;
            for (int i = 0; i < BitsPerOctet; i++)
            {
                crc = (ushort)((crc >> 1) ^ ((crc & 1) != 0 ? ReflectedPolynomial : 0));
            }
        }

        return (ushort)~crc;
    }
}
