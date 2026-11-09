using System.Collections.Immutable;
using EMCL.Framing.Hdlc.Frames;
using EMCL.Framing.Hdlc.Llc;

namespace EMCL.Framing;

/// <summary>Encodes local SNRM proposals and matches peer-direction UA limits to local limits.</summary>
public static class HdlcNegotiation
{
    private const byte FormatIdentifier = 0x81;
    private const byte GroupIdentifier = 0x80;
    private const byte MaximumTransmitInformation = 0x05;
    private const byte MaximumReceiveInformation = 0x06;
    private const byte TransmitWindow = 0x07;
    private const byte ReceiveWindow = 0x08;
    private const int HeaderLength = 3;
    private const int BitsPerOctet = 8;
    /// <summary>Maximum supported HDLC information-field size in bytes, including LLC.</summary>
    public const int MaximumInformationBytes = 2030;
    private const int MaximumWindow = HdlcFrameFormat.SequenceMask;

    /// <summary>Encodes all four local parameters; null preserves the legacy empty SNRM information field.</summary>
    /// <param name="proposed">Local limits in bytes and frames. Only one-frame windows are supported.</param>
    /// <returns>An owned immutable parameter group.</returns>
    public static ImmutableArray<byte> CreateSnrmInformation(HdlcLinkParameters? proposed)
    {
        if (proposed is null)
        {
            return [];
        }

        proposed.ValidateProposal();
        // Use the two-octet MAX INFO layout when either size exceeds the default range.
        bool extended = proposed.MaximumTransmitInformationBytes > HdlcFrameFormat.DefaultInformationLength
            || proposed.MaximumReceiveInformationBytes > HdlcFrameFormat.DefaultInformationLength;
        var bytes = new List<byte> { FormatIdentifier, GroupIdentifier, 0 };
        AddParameter(bytes, MaximumTransmitInformation, proposed.MaximumTransmitInformationBytes, extended ? sizeof(ushort) : sizeof(byte));
        AddParameter(bytes, MaximumReceiveInformation, proposed.MaximumReceiveInformationBytes, extended ? sizeof(ushort) : sizeof(byte));
        AddParameter(bytes, TransmitWindow, proposed.TransmitWindowFrames, sizeof(uint));
        AddParameter(bytes, ReceiveWindow, proposed.ReceiveWindowFrames, sizeof(uint));
        bytes[2] = checked((byte)(bytes.Count - HeaderLength));
        return [.. bytes];
    }

    /// <summary>Matches local TX to peer RX and local RX to peer TX; absent parameters use protocol defaults.</summary>
    /// <param name="proposed">The exact local proposal, or null for an empty SNRM.</param>
    /// <param name="information">The peer's UA information field.</param>
    /// <returns>Separate proposed, received, and effective values, including the original UA bytes.</returns>
    /// <exception cref="InvalidDataException">The UA is malformed, its windows exceed one frame, or its effective sizes cannot carry LLC.</exception>
    public static HdlcNegotiationResult Negotiate(HdlcLinkParameters? proposed, ImmutableArray<byte> information)
    {
        proposed?.ValidateProposal();
        var received = Parse(information);
        var peer = new HdlcLinkParameters(
            received?.MaximumTransmitInformationBytes ?? HdlcFrameFormat.DefaultInformationLength,
            received?.MaximumReceiveInformationBytes ?? HdlcFrameFormat.DefaultInformationLength,
            received?.TransmitWindowFrames ?? 1, received?.ReceiveWindowFrames ?? 1);
        var local = proposed ?? new HdlcLinkParameters();
        if (peer.TransmitWindowFrames != 1 || peer.ReceiveWindowFrames != 1)
        {
            throw new InvalidDataException("HDLC UA windows must be one frame for stop-and-wait exchange.");
        }

        var effective = new HdlcLinkParameters(
            Math.Min(local.MaximumTransmitInformationBytes, peer.MaximumReceiveInformationBytes),
            Math.Min(local.MaximumReceiveInformationBytes, peer.MaximumTransmitInformationBytes));
        if (effective.MaximumTransmitInformationBytes < LlcHeader.Length
            || effective.MaximumReceiveInformationBytes < LlcHeader.Length)
        {
            throw new InvalidDataException("HDLC UA information limits cannot carry an LLC header.");
        }

        return new(proposed, received, effective, information.IsDefault ? [] : information);
    }

    /// <summary>Returns the legacy outgoing limit for an empty SNRM, capped at 128 bytes.
    /// Use <see cref="Negotiate"/> to account for an explicit local proposal and validate stop-and-wait compatibility.</summary>
    /// <param name="information">The UA information field.</param>
    /// <returns>The maximum number of outgoing information octets.</returns>
    public static int MaximumOutgoingInformation(ImmutableArray<byte> information)
        => Math.Min(Parse(information)?.MaximumReceiveInformationBytes ?? HdlcFrameFormat.DefaultInformationLength,
            HdlcFrameFormat.DefaultInformationLength);

    private static HdlcUaParameters? Parse(ImmutableArray<byte> information)
    {
        if (information.IsDefaultOrEmpty)
        {
            return null;
        }

        ReadOnlySpan<byte> data = information.AsSpan();
        if (data.Length < HeaderLength || data[0] != FormatIdentifier || data[1] != GroupIdentifier
            || data[2] != data.Length - HeaderLength)
        {
            throw new InvalidDataException("Invalid HDLC UA parameter group.");
        }

        var parameters = new HdlcUaParameters();
        var seen = new HashSet<byte>();
        for (int offset = HeaderLength; offset < data.Length;)
        {
            var (identifier, length, value) = ReadParameter(data, ref offset, seen);
            ValidateParameter(identifier, length, value);
            parameters = identifier switch
            {
                MaximumTransmitInformation => parameters with { MaximumTransmitInformationBytes = (int)value },
                MaximumReceiveInformation => parameters with { MaximumReceiveInformationBytes = (int)value },
                TransmitWindow => parameters with { TransmitWindowFrames = (int)value },
                ReceiveWindow => parameters with { ReceiveWindowFrames = (int)value },
                _ => parameters
            };
        }

        return parameters;
    }

    private static void AddParameter(List<byte> bytes, byte identifier, int value, int length)
    {
        bytes.Add(identifier);
        bytes.Add((byte)length);
        for (int shift = (length - 1) * BitsPerOctet; shift >= 0; shift -= BitsPerOctet)
        {
            bytes.Add((byte)(value >> shift));
        }
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
            value = value << BitsPerOctet | data[offset++];
        }

        return (identifier, length, value);
    }

    private static void ValidateParameter(byte identifier, int length, uint value)
    {
        bool supported = identifier switch
        {
            MaximumTransmitInformation or MaximumReceiveInformation => length is 1 or 2
                && value is > 0 and <= MaximumInformationBytes,
            TransmitWindow or ReceiveWindow => length == sizeof(uint) && value is > 0 and <= MaximumWindow,
            _ => false
        };
        if (!supported)
        {
            throw new InvalidDataException("Unsupported HDLC UA parameter.");
        }
    }
}
