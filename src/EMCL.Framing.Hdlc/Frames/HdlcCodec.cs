using System.Buffers;
using System.Collections.Immutable;
using EMCL.Core.Codecs;

namespace EMCL.Framing.Hdlc.Frames;

/// <summary>Encodes and incrementally decodes HDLC type-3 frames, validating addresses, HCS, FCS, and frame boundaries.</summary>
public sealed class HdlcCodec : IIncrementalCodec<HdlcFrame>
{
    /// <summary>Allows a received frame to end at its validated FCS without a closing flag.</summary>
    /// <remarks>The opening flag is required. Encoding always writes both flags.</remarks>
    public bool AllowMissingClosingFlag
    {
        get; init;
    }

    /// <inheritdoc />
    public DecodeResult<HdlcFrame> Decode(ReadOnlySequence<byte> input) => Decode(input, out _, out _);

    /// <summary>Decodes a frame and supplies checksum failure details for stream recovery.</summary>
    /// <param name="input">The stream bytes starting at an opening flag.</param>
    /// <param name="checksumFailed">Whether an HCS or FCS mismatch caused the failure.</param>
    /// <param name="verifiedFrameEnd">On an FCS failure with a valid HCS, the offset immediately after the FCS.</param>
    /// <returns>The normal incremental decode result.</returns>
    public DecodeResult<HdlcFrame> Decode(ReadOnlySequence<byte> input, out bool checksumFailed, out long? verifiedFrameEnd)
    {
        checksumFailed = false;
        verifiedFrameEnd = null;
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

        while (reader.TryPeek(out byte next) && next == HdlcFrameFormat.Flag)
        {
            reader.Advance(1);
        }

        Span<byte> format = stackalloc byte[HdlcFrameFormat.FormatFieldLength];
        if (!reader.TryCopyTo(format))
        {
            return new(DecodeStatus.NeedMoreData);
        }

        if ((format[0] & HdlcFrameFormat.FormatTypeMask) != HdlcFrameFormat.FormatType3)
        {
            return new(DecodeStatus.Unsupported, Error: "Only HDLC format type 3 is supported.");
        }

        int length = ((format[0] & HdlcFrameFormat.LengthHighMask) << 8) | format[1];
        if (length < HdlcFrameFormat.MinimumFrameLength)
        {
            return new(DecodeStatus.InvalidData, Error: "HDLC frame is too short.");
        }

        long frameEnd = reader.Consumed + length;
        bool completeBody = reader.Remaining >= length;
        // A bad HCS can mean the length itself was corrupted. Check it before waiting for the body.
        const int maximumHeader = HdlcFrameFormat.FormatFieldLength + 2 * HdlcAddressFormat.ExtendedServerLength
            + HdlcFrameFormat.ControlFieldLength + HdlcFrameFormat.ChecksumLength;
        Span<byte> header = stackalloc byte[(int)Math.Min(reader.Remaining,
            Math.Min(maximumHeader, length - HdlcFrameFormat.ChecksumLength))];
        reader.TryCopyTo(header);
        int position = HdlcFrameFormat.FormatFieldLength;
        try
        {
            var addresses = header[..^HdlcFrameFormat.ControlFieldLength];
            var destination = ReadAddress(addresses, ref position);
            var source = ReadAddress(addresses, ref position);
            byte control = header[position++];
            int available = length - position - HdlcFrameFormat.ChecksumLength;
            if (available is > 0 and <= HdlcFrameFormat.ChecksumLength)
            {
                throw new FormatException("Invalid HCS/information length.");
            }

            if (available > 0)
            {
                if (header.Length < position + HdlcFrameFormat.ChecksumLength)
                {
                    return new(DecodeStatus.NeedMoreData);
                }

                if (ReadCrc(header, position) != HdlcChecksum.Compute(header[..position]))
                {
                    checksumFailed = true;
                    return new(DecodeStatus.InvalidData, Error: "Invalid HDLC HCS.");
                }

                position += HdlcFrameFormat.ChecksumLength;
            }

            if (!completeBody)
            {
                return new(DecodeStatus.NeedMoreData);
            }

            if (!AllowMissingClosingFlag)
            {
                reader.Advance(length);
                if (!reader.TryPeek(out byte closingFlag))
                {
                    return new(DecodeStatus.NeedMoreData);
                }

                if (closingFlag != HdlcFrameFormat.Flag)
                {
                    int nearbyLength = Math.Min(length, 8);
                    string nearClosing = Convert.ToHexString(input.Slice(frameEnd - nearbyLength, nearbyLength + 1).ToArray());
                    return new(DecodeStatus.InvalidData, Error:
                        $"Missing HDLC closing flag at offset {frameEnd} " +
                        $"(declared length {length}, found {closingFlag:X2}, near closing={nearClosing}).");
                }
            }

            byte[] data = input.Slice(frameEnd - length, length).ToArray();
            if (ReadCrc(data, length - HdlcFrameFormat.ChecksumLength) != HdlcChecksum.Compute(data.AsSpan(0, length - HdlcFrameFormat.ChecksumLength)))
            {
                checksumFailed = true;
                if (available > 0)
                {
                    verifiedFrameEnd = frameEnd;
                }

                return new(DecodeStatus.InvalidData, Error: "Invalid HDLC FCS.");
            }

            if (source.Length != HdlcAddressFormat.ClientLength && destination.Length != HdlcAddressFormat.ClientLength)
            {
                throw new FormatException("One address must be a one-octet client address.");
            }

            ImmutableArray<byte> information = available > 0
                ? data.AsSpan(position, length - position - HdlcFrameFormat.ChecksumLength).ToArray().ToImmutableArray()
                : [];
            // Leave the next octet unread. A closing flag may also open the next frame.
            return DecodeResult<HdlcFrame>.Done(new(destination, source, control, information, (format[0] & HdlcFrameFormat.SegmentationMask) != 0), frameEnd);
        }
        catch (FormatException e)
        {
            return completeBody ? new(DecodeStatus.InvalidData, Error: e.Message) : new(DecodeStatus.NeedMoreData);
        }
    }

    private static ushort ReadCrc(ReadOnlySpan<byte> data, int index) => (ushort)(data[index] | data[index + 1] << 8);
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
