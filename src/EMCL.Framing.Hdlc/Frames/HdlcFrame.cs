using System.Collections.Immutable;

namespace EMCL.Framing.Hdlc.Frames;

/// <summary>Represents an HDLC format-type-3 frame independently of the application data it carries.</summary>
/// <param name="Destination">The destination HDLC address.</param>
/// <param name="Source">The source HDLC address.</param>
/// <param name="Control">The original HDLC control octet.</param>
/// <param name="Information">The information-field octets without frame headers or checksums.</param>
/// <param name="Segmented">Whether more information-field segments follow.</param>
public sealed record HdlcFrame(HdlcAddress Destination, HdlcAddress Source, byte Control, ImmutableArray<byte> Information, bool Segmented = false)
{
    /// <summary>Gets whether the control octet identifies an I-frame.</summary>
    public bool IsInformation => (Control & HdlcFrameFormat.InformationTypeMask) == 0;
    /// <summary>Gets whether the control octet identifies a UI-frame, ignoring the poll/final bit.</summary>
    public bool IsUnnumberedInformation => (Control & HdlcFrameFormat.ControlWithoutPollFinalMask) == HdlcFrameFormat.UnnumberedInformationControl;
    /// <summary>Gets the three-bit send sequence number encoded in the control octet.</summary>
    public byte SendSequence => (byte)((Control >> HdlcFrameFormat.SendSequenceShift) & HdlcFrameFormat.SequenceMask);
    /// <summary>Gets the three-bit receive sequence number encoded in the control octet.</summary>
    public byte ReceiveSequence => (byte)((Control >> HdlcFrameFormat.ReceiveSequenceShift) & HdlcFrameFormat.SequenceMask);
}
