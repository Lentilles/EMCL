using System.Collections.Immutable;
using EMCL.Framing.Hdlc.Frames;

namespace EMCL.Framing;

/// <summary>Parses HDLC UA link parameters used to bound outgoing information fields.</summary>
public static class HdlcNegotiation
{
    private const byte FormatIdentifier = 0x81;
    private const byte GroupIdentifier = 0x80;
    private const byte MaximumTransmitInformation = 0x05;
    private const byte MaximumReceiveInformation = 0x06;
    private const byte TransmitWindow = 0x07;
    private const byte ReceiveWindow = 0x08;
    private const int HeaderLength = 3;
    private const int MaximumInformationLength = 2030;
    private const int MaximumWindow = HdlcFrameFormat.SequenceMask;

    /// <summary>Returns the accepted outgoing information size, capped at the safe default.</summary>
    /// <param name="information">The UA information field.</param>
    /// <returns>The maximum number of outgoing information octets.</returns>
    public static int MaximumOutgoingInformation(ImmutableArray<byte> information)
    {
        if (information.IsDefaultOrEmpty)
        {
            return HdlcFrameFormat.DefaultInformationLength;
        }

        ReadOnlySpan<byte> data = information.AsSpan();
        if (data.Length < HeaderLength || data[0] != FormatIdentifier || data[1] != GroupIdentifier
            || data[2] != data.Length - HeaderLength)
        {
            throw new InvalidDataException("Invalid HDLC UA parameter group.");
        }

        int maximumReceive = HdlcFrameFormat.DefaultInformationLength;
        var seen = new HashSet<byte>();
        for (int offset = HeaderLength; offset < data.Length;)
        {
            var (identifier, length, value) = ReadParameter(data, ref offset, seen);
            ValidateParameter(identifier, length, value);
            if (identifier == MaximumReceiveInformation)
            {
                maximumReceive = (int)value;
            }
        }

        return Math.Min(maximumReceive, HdlcFrameFormat.DefaultInformationLength);
    }

    private static (byte Identifier, int Length, uint Value) ReadParameter(ReadOnlySpan<byte> data, ref int offset,
        HashSet<byte> seen)
    {
        if (data.Length - offset < 2)
        {
            throw new InvalidDataException("Truncated HDLC UA parameter.");
        }

        byte identifier = data[offset++];
        int length = data[offset++];
        if (length is 0 or > sizeof(uint) || length > data.Length - offset || !seen.Add(identifier))
        {
            throw new InvalidDataException("Invalid or repeated HDLC UA parameter.");
        }

        uint value = 0;
        for (int i = 0; i < length; i++)
        {
            value = value << 8 | data[offset++];
        }

        return (identifier, length, value);
    }

    private static void ValidateParameter(byte identifier, int length, uint value)
    {
        bool supported = identifier switch
        {
            MaximumTransmitInformation or MaximumReceiveInformation => length is 1 or 2
                && value is > 0 and <= MaximumInformationLength,
            TransmitWindow or ReceiveWindow => length == sizeof(uint) && value is > 0 and <= MaximumWindow,
            _ => false
        };
        if (!supported)
        {
            throw new InvalidDataException("Unsupported HDLC UA parameter.");
        }
    }
}
