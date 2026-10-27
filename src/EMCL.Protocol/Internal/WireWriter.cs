using System.Buffers.Binary;
using System.Buffers;

namespace EMCL.Protocol.Internal;

internal static class WireWriter
{
    public static void Byte(IBufferWriter<byte> w, byte b)
    {
        w.GetSpan(1)[0] = b;
        w.Advance(1);
    }

    public static void Bytes(IBufferWriter<byte> w, ReadOnlySpan<byte> bytes)
    {
        if (bytes.IsEmpty)
        {
            return;
        }

        bytes.CopyTo(w.GetSpan(bytes.Length));
        w.Advance(bytes.Length);
    }

    public static void U16(IBufferWriter<byte> w, ushort n)
    {
        BinaryPrimitives.WriteUInt16BigEndian(w.GetSpan(sizeof(ushort)), n);
        w.Advance(sizeof(ushort));
    }

    public static void U32(IBufferWriter<byte> w, uint n)
    {
        BinaryPrimitives.WriteUInt32BigEndian(w.GetSpan(sizeof(uint)), n);
        w.Advance(sizeof(uint));
    }

    public static void U64(IBufferWriter<byte> w, ulong n)
    {
        BinaryPrimitives.WriteUInt64BigEndian(w.GetSpan(sizeof(ulong)), n);
        w.Advance(sizeof(ulong));
    }

    public static void Length(IBufferWriter<byte> w, int n)
    {
        if (n < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(n));
        }

        if (n < BerEncoding.LongLengthMask)
        {
            Byte(w, (byte)n);
            return;
        }

        int count = n <= byte.MaxValue ? 1 : n <= ushort.MaxValue ? 2 : n <= BerEncoding.MaximumThreeOctetLength ? 3 : 4;
        Byte(w, (byte)(BerEncoding.LongLengthMask | count));
        for (int i = count - 1; i >= 0; i--)
        {
            Byte(w, (byte)(n >> (8 * i)));
        }
    }
}
