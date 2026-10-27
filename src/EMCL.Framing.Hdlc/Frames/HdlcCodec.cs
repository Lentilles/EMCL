using System.Buffers;
using System.Collections.Immutable;
using EMCL.Core.Codecs;

namespace EMCL.Framing.Hdlc.Frames;

/// <summary>Encodes and incrementally decodes HDLC type-3 frames, validating addresses, HCS, FCS, and frame boundaries.</summary>
public sealed class HdlcCodec : IIncrementalCodec<HdlcFrame>
{
    /// <inheritdoc />
    public DecodeResult<HdlcFrame> Decode(ReadOnlySequence<byte> input)
    {
        // Length field bounds frames to 2047 octets; repeated idle flags are not copied.
        var reader = new SequenceReader<byte>(input);
        if (!reader.TryRead(out byte flag))
        {
            return new(DecodeStatus.NeedMoreData);
        }

        if (flag != HdlcFrameFormat.Flag)
        {
            return new(DecodeStatus.InvalidData, Error: "Expected HDLC flag.");
        }

        long start = 0;
        while (reader.TryPeek(out byte next) && next == HdlcFrameFormat.Flag)
        {
            reader.Advance(1);
            start++;
        }

        if (reader.Remaining < HdlcFrameFormat.FormatFieldLength)
        {
            return new(DecodeStatus.NeedMoreData);
        }

        Span<byte> format = stackalloc byte[HdlcFrameFormat.FormatFieldLength];
        input.Slice(start + 1, HdlcFrameFormat.FormatFieldLength).CopyTo(format);
        if ((format[0] & HdlcFrameFormat.FormatTypeMask) != HdlcFrameFormat.FormatType3)
        {
            return new(DecodeStatus.Unsupported, Error: "Only HDLC format type 3 is supported.");
        }

        int length = ((format[0] & HdlcFrameFormat.LengthHighMask) << 8) | format[1];
        if (length < HdlcFrameFormat.MinimumFrameLength)
        {
            return new(DecodeStatus.InvalidData, Error: "HDLC frame is too short.");
        }

        if (input.Length < start + length + HdlcFrameFormat.BoundaryFlagsLength)
        {
            return new(DecodeStatus.NeedMoreData);
        }

        byte[] data = input.Slice(start + 1, length + 1).ToArray();
        if (data[length] != HdlcFrameFormat.Flag)
        {
            return new(DecodeStatus.InvalidData, Error: "Missing HDLC closing flag.");
        }

        int position = HdlcFrameFormat.FormatFieldLength;
        try
        {
            var destination = ReadAddress(data.AsSpan(0, length - HdlcFrameFormat.ChecksumLength - HdlcFrameFormat.ControlFieldLength), ref position);
            var source = ReadAddress(data.AsSpan(0, length - HdlcFrameFormat.ChecksumLength - HdlcFrameFormat.ControlFieldLength), ref position);
            if (position >= length - HdlcFrameFormat.ChecksumLength)
            {
                throw new FormatException("Missing control field.");
            }

            byte control = data[position++];
            int available = length - position - HdlcFrameFormat.ChecksumLength;
            if (available is > 0 and <= HdlcFrameFormat.ChecksumLength)
            {
                throw new FormatException("Invalid HCS/information length.");
            }

            ImmutableArray<byte> information = [];
            if (available > 0)
            {
                if (ReadCrc(data, position) != HdlcChecksum.Compute(data.AsSpan(0, position)))
                {
                    throw new FormatException("Invalid HDLC HCS.");
                }

                position += HdlcFrameFormat.ChecksumLength;
                information = data.AsSpan(position, length - position - HdlcFrameFormat.ChecksumLength).ToArray().ToImmutableArray();
            }

            if (ReadCrc(data, length - HdlcFrameFormat.ChecksumLength) != HdlcChecksum.Compute(data.AsSpan(0, length - HdlcFrameFormat.ChecksumLength)))
            {
                throw new FormatException("Invalid HDLC FCS.");
            }

            if (source.Length != HdlcAddressFormat.ClientLength && destination.Length != HdlcAddressFormat.ClientLength)
            {
                throw new FormatException("One address must be a one-octet client address.");
            }

            // Retain the closing flag: it may also open the next frame.
            return DecodeResult<HdlcFrame>.Done(new(destination, source, control, information, (format[0] & HdlcFrameFormat.SegmentationMask) != 0), start + length + 1);
        }
        catch (FormatException e)
        {
            return new(DecodeStatus.InvalidData, Error: e.Message);
        }
    }

    private static ushort ReadCrc(byte[] data, int index) => (ushort)(data[index] | data[index + 1] << 8);
    private static HdlcAddress ReadAddress(ReadOnlySpan<byte> data, ref int position)
    {
        uint value = 0;
        int count = 0;
        while (position < data.Length && count < HdlcAddressFormat.ExtendedServerLength)
        {
            byte b = data[position++];
            count++;
            value = value << HdlcAddressFormat.GroupBits | (uint)(b >> 1);
            if ((b & HdlcAddressFormat.LastOctetMask) != 0)
            {
                if (count == HdlcAddressFormat.ExtendedServerLength - 1)
                {
                    throw new FormatException("Three-octet HDLC addresses are not supported.");
                }

                return new(value, (byte)count);
            }
        }

        throw new FormatException("Malformed HDLC address.");
    }

    /// <inheritdoc />
    public void Encode(HdlcFrame value, IBufferWriter<byte> output)
    {
        ValidateAddress(value.Destination);
        ValidateAddress(value.Source);
        if (value.Source.Length != HdlcAddressFormat.ClientLength && value.Destination.Length != HdlcAddressFormat.ClientLength)
        {
            throw new ArgumentException("A client address must have one octet.");
        }

        bool information = !value.Information.IsEmpty;
        int length = HdlcFrameFormat.FormatFieldLength + value.Destination.Length + value.Source.Length
            + HdlcFrameFormat.ControlFieldLength + (information ? HdlcFrameFormat.ChecksumLength : 0)
            + value.Information.Length + HdlcFrameFormat.ChecksumLength;
        if (length > HdlcFrameFormat.MaximumFrameLength)
        {
            throw new ArgumentException("HDLC frame exceeds eleven-bit length.");
        }

        var frame = new byte[length + HdlcFrameFormat.BoundaryFlagsLength];
        frame[0] = HdlcFrameFormat.Flag;
        frame[^1] = HdlcFrameFormat.Flag;
        var body = frame.AsSpan(1, length);
        body[0] = (byte)(HdlcFrameFormat.FormatType3 | (value.Segmented ? HdlcFrameFormat.SegmentationMask : 0) | (length >> 8));
        body[1] = (byte)length;
        int position = HdlcFrameFormat.FormatFieldLength;
        WriteAddress(value.Destination, body, ref position);
        WriteAddress(value.Source, body, ref position);
        body[position++] = value.Control;
        if (information)
        {
            ushort hcs = HdlcChecksum.Compute(body[..position]);
            body[position++] = (byte)hcs;
            body[position++] = (byte)(hcs >> 8);
            value.Information.AsSpan().CopyTo(body[position..]);
            position += value.Information.Length;
        }

        ushort fcs = HdlcChecksum.Compute(body[..position]);
        body[position++] = (byte)fcs;
        body[position] = (byte)(fcs >> 8);
        frame.CopyTo(output.GetSpan(frame.Length));
        output.Advance(frame.Length);
    }

    private static void ValidateAddress(HdlcAddress address)
    {
        if (address.Length is not (HdlcAddressFormat.ClientLength or HdlcAddressFormat.ServerLength or HdlcAddressFormat.ExtendedServerLength) || address.Value >= 1u << (HdlcAddressFormat.GroupBits * address.Length))
        {
            throw new ArgumentException("Invalid HDLC address.");
        }
    }

    private static void WriteAddress(HdlcAddress address, Span<byte> output, ref int position)
    {
        for (int i = address.Length - 1; i >= 0; i--)
        {
            output[position++] = (byte)(((address.Value >> (HdlcAddressFormat.GroupBits * i)) & HdlcAddressFormat.GroupMask) << 1 | (i == 0 ? HdlcAddressFormat.LastOctetMask : 0u));
        }
    }
}
