using System.Buffers.Binary;
using EMCL.Core.Codecs;

namespace EMCL.Protocol.Internal;

internal ref struct WireReader(ReadOnlySpan<byte> bytes)
{
    private readonly ReadOnlySpan<byte> data = bytes;
    public int Position
    {
        get; private set;
    }
    public int Remaining => data.Length - Position;

    public byte Byte() => Take(1)[0];
    public ReadOnlySpan<byte> Take(int count)
    {
        if (count < 0)
        {
            throw new WireException(DecodeStatus.InvalidData, "Negative length.");
        }

        if (count > Remaining)
        {
            throw new WireException(DecodeStatus.NeedMoreData, "Truncated input.");
        }

        var result = data.Slice(Position, count);
        Position += count;
        return result;
    }

    public int Length(int limit)
    {
        byte b = Byte();
        if (b < BerEncoding.LongLengthMask)
        {
            return b <= limit ? b : throw new WireException(DecodeStatus.InvalidData, "Length limit exceeded.");
        }

        int n = b & BerEncoding.LengthOctetCountMask;
        if (n is 0 or > BerEncoding.MaximumLengthOctets)
        {
            throw new WireException(DecodeStatus.InvalidData, "Invalid definite length.");
        }

        uint value = 0;
        for (int i = 0; i < n; i++)
        {
            value = (value << 8) | Byte();
        }

        if (value > limit)
        {
            throw new WireException(DecodeStatus.InvalidData, "Length limit exceeded.");
        }

        return (int)value;
    }

    public ushort U16() => BinaryPrimitives.ReadUInt16BigEndian(Take(sizeof(ushort)));
    public uint U32() => BinaryPrimitives.ReadUInt32BigEndian(Take(sizeof(uint)));
    public ulong U64() => BinaryPrimitives.ReadUInt64BigEndian(Take(sizeof(ulong)));
}
