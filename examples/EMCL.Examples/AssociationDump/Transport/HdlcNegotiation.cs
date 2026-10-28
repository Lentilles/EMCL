using System.Collections.Immutable;
using EMCL.Framing.Hdlc.Frames;

namespace EMCL.Examples.AssociationDump.Transport;

internal static class HdlcNegotiation
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

    internal static int MaximumOutgoingInformation(ImmutableArray<byte> information)
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

            switch (identifier)
            {
                case MaximumTransmitInformation or MaximumReceiveInformation when length is 1 or 2
                    && value is > 0 and <= MaximumInformationLength:
                    if (identifier == MaximumReceiveInformation)
                    {
                        maximumReceive = (int)value;
                    }

                    break;
                case TransmitWindow or ReceiveWindow when length == sizeof(uint)
                    && value is > 0 and <= MaximumWindow:
                    break;
                default:
                    throw new InvalidDataException("Unsupported HDLC UA parameter.");
            }
        }

        return Math.Min(maximumReceive, HdlcFrameFormat.DefaultInformationLength);
    }
}
