using System.Buffers;
using System.Collections.Immutable;
using EMCL.Core.Codecs;
using EMCL.Framing.Hdlc.Frames;
using EMCL.Framing.Hdlc.Llc;
using EMCL.Framing.Hdlc.Segmentation;
using EMCL.Transport.Channels;

namespace EMCL.Framing;

/// <summary>Maps HDLC data frames to complete APDUs by assembling segments before processing LLC.</summary>
/// <remarks>The application supplies negotiated frame limits and manages sequence acknowledgements, link establishment, and retries. The adapter assembles segments before decoding LLC.</remarks>
public sealed class HdlcApduAdapter : IFramePayloadAdapter<HdlcFrame>
{
    /// <inheritdoc />
    public int IdleBytesToRetain => 1;

    private readonly HdlcAddress local, remote;
    private readonly bool response;
    private readonly int maxInformationBytes;
    private readonly HdlcSegmentAssembler assembler;
    private readonly LlcCodec llc;
    /// <summary>Gets or sets the next three-bit send sequence number; the application manages link state.</summary>
    public byte NextSendSequence
    {
        get; set;
    }
    /// <summary>Gets or sets the three-bit receive acknowledgement number encoded in outgoing frames.</summary>
    public byte ReceiveSequence
    {
        get; set;
    }
    /// <summary>Gets or sets whether outgoing frames set the poll/final bit.</summary>
    public bool PollFinal { get; set; } = true;
    /// <summary>Gets or sets the optional callback for received supervisory and link-control frames.</summary>
    public Action<HdlcFrame>? OnControlFrame
    {
        get; set;
    }
    /// <inheritdoc />
    public bool HasPending => assembler.HasPending;

    /// <summary>Creates an HDLC data adapter using caller-selected addresses, sequence state, and negotiated information-field size.</summary>
    /// <param name="local">The local HDLC address.</param>
    /// <param name="remote">The expected remote HDLC address.</param>
    /// <param name="response">Whether to create response-direction framing or a release response.</param>
    /// <param name="maxInformationBytes">The negotiated maximum information-field size per outgoing HDLC frame.</param>
    /// <param name="limits">Optional resource bounds; null selects validated defaults.</param>
    public HdlcApduAdapter(HdlcAddress local, HdlcAddress remote, bool response = false, int maxInformationBytes = HdlcFrameFormat.DefaultInformationLength, ProtocolLimits? limits = null)
    {
        int maximum = HdlcFrameFormat.MaximumFrameLength - (HdlcFrameFormat.FormatFieldLength + local.Length + remote.Length
            + HdlcFrameFormat.ControlFieldLength + 2 * HdlcFrameFormat.ChecksumLength);
        if (maxInformationBytes < LlcHeader.Length || maxInformationBytes > maximum)
        {
            throw new ArgumentOutOfRangeException(nameof(maxInformationBytes));
        }

        this.local = local;
        this.remote = remote;
        this.response = response;
        this.maxInformationBytes = maxInformationBytes;
        assembler = new(limits);
        llc = new(limits);
    }

    /// <inheritdoc />
    public ReadOnlyMemory<byte>? Accept(HdlcFrame frame)
    {
        if (frame.Destination != local || frame.Source != remote)
        {
            throw new InvalidDataException("Unexpected HDLC addresses.");
        }

        if (!frame.IsInformation && !frame.IsUnnumberedInformation)
        {
            OnControlFrame?.Invoke(frame);
        }

        var information = assembler.Accept(frame);
        if (!information.HasValue)
        {
            return null;
        }

        var message = llc.Decode(new ReadOnlySequence<byte>(information.Value.ToArray()));
        if (message.Status != DecodeStatus.Done)
        {
            throw new InvalidDataException($"LLC decode failed: {message.Error}");
        }

        return message.Value!.Apdu.ToArray();
    }

    /// <inheritdoc />
    public IEnumerable<HdlcFrame> Frame(ReadOnlyMemory<byte> apdu)
    {
        byte[] information = llc.EncodeBytes(new(response, apdu.ToArray().ToImmutableArray()));
        for (int offset = 0; offset < information.Length; offset += maxInformationBytes)
        {
            if (NextSendSequence > HdlcFrameFormat.SequenceMask || ReceiveSequence > HdlcFrameFormat.SequenceMask)
            {
                throw new InvalidOperationException("HDLC sequence numbers are three bits.");
            }

            int length = Math.Min(maxInformationBytes, information.Length - offset);
            bool segmented = offset + length < information.Length;
            byte control = (byte)(NextSendSequence << HdlcFrameFormat.SendSequenceShift | ReceiveSequence << HdlcFrameFormat.ReceiveSequenceShift | (PollFinal ? HdlcFrameFormat.PollFinalMask : 0));
            NextSendSequence = (byte)((NextSendSequence + 1) & HdlcFrameFormat.SequenceMask);
            yield return new(remote, local, control, information.AsSpan(offset, length).ToArray().ToImmutableArray(), segmented);
        }
    }

    /// <inheritdoc />
    public bool IsIdle(ReadOnlySpan<byte> buffered)
    {
        foreach (byte b in buffered)
        {
            if (b != HdlcFrameFormat.Flag)
            {
                return false;
            }
        }

        return true;
    }
}
