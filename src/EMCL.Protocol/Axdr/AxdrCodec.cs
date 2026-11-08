using System.Buffers;
using System.Collections.Immutable;
using System.Text;
using EMCL.Core.Codecs;
using EMCL.Core.Values;
using EMCL.Protocol.Internal;

namespace EMCL.Protocol.Axdr;

/// <summary>Encodes and incrementally decodes A-XDR Data values, including compact arrays, within configured resource bounds.</summary>
public sealed class AxdrCodec : IIncrementalCodec<DlmsValue>
{
    private readonly ProtocolLimits limits;
    private static readonly UTF8Encoding Utf8 = new(false, true);
    /// <summary>Creates a codec using the supplied limits or validated default limits.</summary>
    /// <param name="limits">Optional resource bounds; null selects validated defaults.</param>
    public AxdrCodec(ProtocolLimits? limits = null)
    {
        this.limits = ProtocolLimits.Resolve(limits);
    }

    /// <inheritdoc />
    public DecodeResult<DlmsValue> Decode(ReadOnlySequence<byte> input)
    {
        // Only inspect a bounded prefix: callers may supply several concatenated values.
        var data = input.Slice(0, Math.Min(input.Length, (long)limits.MaxMessageBytes + 1)).ToArray();
        var reader = new WireReader(data);
        try
        {
            int budget = limits.MaxCollectionItems;
            var value = Read(ref reader, 0, ref budget);
            if (reader.Position > limits.MaxMessageBytes)
            {
                return new(DecodeStatus.InvalidData, Error: "Message limit exceeded.");
            }

            return DecodeResult<DlmsValue>.Done(value, reader.Position);
        }
        catch (WireException e)
        {
            return new(e.Status == DecodeStatus.NeedMoreData && input.Length > limits.MaxMessageBytes ? DecodeStatus.InvalidData : e.Status, Error: e.Message);
        }
        catch (Exception e) when (e is ArgumentException or OverflowException or DecoderFallbackException)
        {
            return new(DecodeStatus.InvalidData, Error: e.Message);
        }
    }

    /// <inheritdoc />
    public void Encode(DlmsValue value, IBufferWriter<byte> output)
    {
        try
        {
            EncodeCore(value, output);
        }
        catch (WireException e)
        {
            throw new ArgumentException(e.Message, nameof(value), e);
        }
    }

    private void EncodeCore(DlmsValue value, IBufferWriter<byte> output)
    {
        var buffer = new BoundedBufferWriter(limits.MaxMessageBytes);
        int budget = limits.MaxCollectionItems;
        Write(value, buffer, 0, ref budget);
        if (buffer.WrittenCount > limits.MaxMessageBytes)
        {
            throw new ArgumentException("Message limit exceeded.");
        }

        WireWriter.Bytes(output, buffer.WrittenSpan);
    }

    internal DlmsValue Read(ref WireReader r, int depth, ref int budget)
    {
        var type = (DlmsType)r.Byte();
        return ReadPayload(ref r, type, depth, ref budget);
    }

    private void Guard(int depth, ref int budget)
    {
        if (depth > limits.MaxDepth || --budget < 0)
        {
            throw new WireException(DecodeStatus.InvalidData, "Value complexity limit exceeded.");
        }
    }

    private DlmsValue ReadPayload(ref WireReader r, DlmsType type, int depth, ref int budget)
    {
        Guard(depth, ref budget);
        switch (type)
        {
            case DlmsType.Null:
                return DlmsValue.Null;
            case DlmsType.Array:
            case DlmsType.Structure:
                {
                    int count = r.Length(limits.MaxCollectionItems);
                    var values = ImmutableArray.CreateBuilder<DlmsValue>(count);
                    for (int i = 0; i < count; i++)
                    {
                        values.Add(Read(ref r, depth + 1, ref budget));
                    }

                    return new(type, values.ToImmutable());
                }

            case DlmsType.Boolean:
                {
                    byte b = r.Byte();
                    if (b > AxdrEncoding.True)
                    {
                        throw new WireException(DecodeStatus.InvalidData, "Invalid boolean.");
                    }

                    return new(type, b == AxdrEncoding.True);
                }

            case DlmsType.BitString:
                {
                    int bits = r.Length(limits.MaxMessageBytes > int.MaxValue / 8 ? int.MaxValue : limits.MaxMessageBytes * 8);
                    return new(type, new DlmsBitString(bits, r.Take((int)((bits + 7L) / 8))));
                }

            case DlmsType.Int8:
            case DlmsType.DeltaInt8:
                return DlmsValue.Number(type, (sbyte)r.Byte());
            case DlmsType.Int16:
            case DlmsType.DeltaInt16:
                return DlmsValue.Number(type, (short)r.U16());
            case DlmsType.Int32:
            case DlmsType.DeltaInt32:
                return DlmsValue.Number(type, (int)r.U32());
            case DlmsType.Int64:
                return DlmsValue.Number(type, (long)r.U64());
            case DlmsType.UInt8:
            case DlmsType.Enum:
            case DlmsType.Bcd:
            case DlmsType.DeltaUInt8:
                return DlmsValue.Number(type, r.Byte());
            case DlmsType.UInt16:
            case DlmsType.DeltaUInt16:
                return DlmsValue.Number(type, r.U16());
            case DlmsType.UInt32:
            case DlmsType.DeltaUInt32:
                return DlmsValue.Number(type, r.U32());
            case DlmsType.UInt64:
                return new(type, r.U64());
            case DlmsType.Float32:
                return new(type, BitConverter.Int32BitsToSingle((int)r.U32()));
            case DlmsType.Float64:
                return new(type, BitConverter.Int64BitsToDouble((long)r.U64()));
            case DlmsType.OctetString:
            case DlmsType.VisibleString:
            case DlmsType.Utf8String:
                {
                    var bytes = r.Take(r.Length(limits.MaxMessageBytes));
                    if (type == DlmsType.OctetString)
                    {
                        return DlmsValue.Octets(bytes);
                    }

                    if (type == DlmsType.VisibleString)
                    {
                        foreach (byte b in bytes)
                        {
                            if (b > sbyte.MaxValue)
                            {
                                throw new WireException(DecodeStatus.InvalidData, "Non-ASCII visible-string.");
                            }
                        }

                        return new(type, Encoding.ASCII.GetString(bytes));
                    }

                    return new(type, Utf8.GetString(bytes));
                }

            case DlmsType.DateTime:
                return new(type, r.Take(DlmsValueSizes.DateTime).ToArray());
            case DlmsType.Date:
                return new(type, r.Take(DlmsValueSizes.Date).ToArray());
            case DlmsType.Time:
                return new(type, r.Take(DlmsValueSizes.Time).ToArray());
            case DlmsType.CompactArray:
                {
                    var description = ReadDescription(ref r, depth + 1, ref budget);
                    var content = new WireReader(r.Take(r.Length(limits.MaxMessageBytes)));
                    var rows = ImmutableArray.CreateBuilder<DlmsValue>();
                    try
                    {
                        while (content.Remaining > 0)
                        {
                            int before = content.Position;
                            rows.Add(ReadCompact(ref content, description, depth + 1, ref budget));
                            if (before == content.Position)
                            {
                                throw new WireException(DecodeStatus.InvalidData, "Zero-width compact row.");
                            }
                        }
                    }
                    catch (WireException e) when (e.Status == DecodeStatus.NeedMoreData)
                    {
                        throw new WireException(DecodeStatus.InvalidData, "Truncated row inside complete compact-array contents.");
                    }

                    return new(type, new DlmsCompactArray(description, rows.ToImmutable()));
                }

            default:
                throw new WireException(DecodeStatus.Unsupported, $"Unsupported A-XDR type {(byte)type}.");
        }
    }

    internal CompactTypeDescription ReadDescription(ref WireReader r, int depth, ref int budget)
    {
        Guard(depth, ref budget);
        var type = (DlmsType)r.Byte();
        if (type == DlmsType.Array)
        {
            ushort count = r.U16();
            return new(type, [ReadDescription(ref r, depth + 1, ref budget)], count);
        }

        if (type == DlmsType.Structure)
        {
            int count = r.Length(limits.MaxCollectionItems);
            var children = ImmutableArray.CreateBuilder<CompactTypeDescription>(count);
            for (int i = 0; i < count; i++)
            {
                children.Add(ReadDescription(ref r, depth + 1, ref budget));
            }

            return new(type, children.ToImmutable());
        }

        if (!Enum.IsDefined(type) || type == DlmsType.CompactArray)
        {
            throw new WireException(DecodeStatus.Unsupported, "Unsupported compact-array element type.");
        }

        return CompactTypeDescription.Scalar(type);
    }

    internal DlmsValue ReadCompact(ref WireReader r, CompactTypeDescription description, int depth, ref int budget)
    {
        if (description.Type is not (DlmsType.Array or DlmsType.Structure))
        {
            return ReadPayload(ref r, description.Type, depth, ref budget);
        }

        Guard(depth, ref budget);
        int count = description.Type == DlmsType.Array ? description.ElementCount : description.Children.Length;
        var values = ImmutableArray.CreateBuilder<DlmsValue>(count);
        for (int i = 0; i < count; i++)
        {
            values.Add(ReadCompact(ref r, description.Children[description.Type == DlmsType.Array ? 0 : i], depth + 1, ref budget));
        }

        return new(description.Type, values.ToImmutable());
    }

    internal void Write(DlmsValue value, IBufferWriter<byte> output, int depth, ref int budget)
    {
        WireWriter.Byte(output, (byte)value.Type);
        WritePayload(value, output, depth, ref budget);
    }

    private void WritePayload(DlmsValue v, IBufferWriter<byte> w, int depth, ref int budget)
    {
        Guard(depth, ref budget);
        switch (v.Type)
        {
            case DlmsType.Null:
                break;
            case DlmsType.Array:
            case DlmsType.Structure:
                WireWriter.Length(w, v.Items.Length);
                foreach (var item in v.Items)
                {
                    Write(item, w, depth + 1, ref budget);
                }

                break;
            case DlmsType.Boolean:
                WireWriter.Byte(w, (bool)v.Value! ? AxdrEncoding.True : AxdrEncoding.False);
                break;
            case DlmsType.BitString:
                var bits = (DlmsBitString)v.Value!;
                WireWriter.Length(w, bits.BitCount);
                WireWriter.Bytes(w, bits.Bytes.AsSpan());
                break;
            case DlmsType.Int8:
            case DlmsType.DeltaInt8:
                WireWriter.Byte(w, unchecked((byte)checked((sbyte)v.AsInt64())));
                break;
            case DlmsType.Int16:
            case DlmsType.DeltaInt16:
                WireWriter.U16(w, unchecked((ushort)checked((short)v.AsInt64())));
                break;
            case DlmsType.Int32:
            case DlmsType.DeltaInt32:
                WireWriter.U32(w, unchecked((uint)checked((int)v.AsInt64())));
                break;
            case DlmsType.Int64:
                WireWriter.U64(w, unchecked((ulong)v.AsInt64()));
                break;
            case DlmsType.UInt8:
            case DlmsType.Enum:
            case DlmsType.Bcd:
            case DlmsType.DeltaUInt8:
                WireWriter.Byte(w, checked((byte)v.AsUInt64()));
                break;
            case DlmsType.UInt16:
            case DlmsType.DeltaUInt16:
                WireWriter.U16(w, checked((ushort)v.AsUInt64()));
                break;
            case DlmsType.UInt32:
            case DlmsType.DeltaUInt32:
                WireWriter.U32(w, checked((uint)v.AsUInt64()));
                break;
            case DlmsType.UInt64:
                WireWriter.U64(w, v.AsUInt64());
                break;
            case DlmsType.Float32:
                WireWriter.U32(w, (uint)BitConverter.SingleToInt32Bits(Convert.ToSingle(v.Value)));
                break;
            case DlmsType.Float64:
                WireWriter.U64(w, (ulong)BitConverter.DoubleToInt64Bits(Convert.ToDouble(v.Value)));
                break;
            case DlmsType.OctetString:
                WireWriter.Length(w, v.Bytes.Length);
                WireWriter.Bytes(w, v.Bytes.AsSpan());
                break;
            case DlmsType.VisibleString:
            case DlmsType.Utf8String:
                var text = (string)v.Value!;
                if (v.Type == DlmsType.VisibleString && text.Any(c => c > sbyte.MaxValue))
                {
                    throw new ArgumentException("Non-ASCII visible-string.");
                }

                if (text.Length > limits.MaxMessageBytes)
                {
                    throw new ArgumentException("String limit exceeded.");
                }

                var bytes = v.Type == DlmsType.VisibleString ? Encoding.ASCII.GetBytes(text) : Utf8.GetBytes(text);
                WireWriter.Length(w, bytes.Length);
                WireWriter.Bytes(w, bytes);
                break;
            case DlmsType.DateTime:
            case DlmsType.Date:
            case DlmsType.Time:
                int size = v.Type == DlmsType.DateTime ? DlmsValueSizes.DateTime : v.Type == DlmsType.Date ? DlmsValueSizes.Date : DlmsValueSizes.Time;
                if (v.Bytes.Length != size)
                {
                    throw new ArgumentException("Invalid date/time length.");
                }

                WireWriter.Bytes(w, v.Bytes.AsSpan());
                break;
            case DlmsType.CompactArray:
                var compact = (DlmsCompactArray)v.Value!;
                WriteDescription(compact.Description, w, depth + 1, ref budget);
                var content = new BoundedBufferWriter(limits.MaxMessageBytes);
                foreach (var row in compact.Rows)
                {
                    int before = content.WrittenCount;
                    WriteCompact(row, compact.Description, content, depth + 1, ref budget);
                    if (content.WrittenCount == before)
                    {
                        throw new ArgumentException("Zero-width compact row.");
                    }
                }

                WireWriter.Length(w, content.WrittenCount);
                WireWriter.Bytes(w, content.WrittenSpan);
                break;
            default:
                throw new NotSupportedException($"Unsupported A-XDR type {v.Type}.");
        }
    }

    private void WriteDescription(CompactTypeDescription d, IBufferWriter<byte> w, int depth, ref int budget)
    {
        Guard(depth, ref budget);
        WireWriter.Byte(w, (byte)d.Type);
        if (d.Type == DlmsType.Array)
        {
            if (d.Children.Length != 1)
            {
                throw new ArgumentException("Array description needs one child.");
            }

            WireWriter.U16(w, d.ElementCount);
            WriteDescription(d.Children[0], w, depth + 1, ref budget);
        }
        else if (d.Type == DlmsType.Structure)
        {
            WireWriter.Length(w, d.Children.Length);
            foreach (var child in d.Children)
            {
                WriteDescription(child, w, depth + 1, ref budget);
            }
        }
        else if (!Enum.IsDefined(d.Type) || d.Type == DlmsType.CompactArray || !d.Children.IsEmpty)
        {
            throw new ArgumentException("Invalid compact-array description.");
        }
    }

    private void WriteCompact(DlmsValue v, CompactTypeDescription d, IBufferWriter<byte> w, int depth, ref int budget)
    {
        if (v.Type != d.Type)
        {
            throw new ArgumentException("Compact-array type mismatch.");
        }

        if (d.Type is not (DlmsType.Array or DlmsType.Structure))
        {
            WritePayload(v, w, depth, ref budget);
            return;
        }

        Guard(depth, ref budget);
        int count = d.Type == DlmsType.Array ? d.ElementCount : d.Children.Length;
        if (v.Items.Length != count)
        {
            throw new ArgumentException("Compact-array element count mismatch.");
        }

        for (int i = 0; i < count; i++)
        {
            WriteCompact(v.Items[i], d.Children[d.Type == DlmsType.Array ? 0 : i], w, depth + 1, ref budget);
        }
    }
}
