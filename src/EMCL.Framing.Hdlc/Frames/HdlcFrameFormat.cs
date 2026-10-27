namespace EMCL.Framing.Hdlc.Frames;

/// <summary>Defines the supported HDLC type-3 frame layout and control-field masks.</summary>
public static class HdlcFrameFormat
{
    /// <summary>The shared opening and closing frame flag.</summary>
    public const byte Flag = 0x7e;
    /// <summary>The frame-format type mask.</summary>
    public const byte FormatTypeMask = 0xf0;
    /// <summary>The supported type-3 frame-format discriminator.</summary>
    public const byte FormatType3 = 0xa0;
    /// <summary>The high three bits of the eleven-bit frame length.</summary>
    public const byte LengthHighMask = 0x07;
    /// <summary>The more-segments bit in the format field.</summary>
    public const byte SegmentationMask = 0x08;
    /// <summary>The maximum eleven-bit frame length, excluding flags.</summary>
    public const int MaximumFrameLength = 2047;
    /// <summary>The minimum frame length with two one-octet addresses.</summary>
    public const int MinimumFrameLength = 7;
    /// <summary>The frame-format field length in octets.</summary>
    public const int FormatFieldLength = sizeof(ushort);
    /// <summary>The HCS or FCS length in octets.</summary>
    public const int ChecksumLength = sizeof(ushort);
    /// <summary>The control field length in octets.</summary>
    public const int ControlFieldLength = sizeof(byte);
    /// <summary>The combined length of the opening and closing flags.</summary>
    public const int BoundaryFlagsLength = 2;
    /// <summary>The default information-field limit before application negotiation.</summary>
    public const int DefaultInformationLength = 128;
    /// <summary>The bit distinguishing information frames.</summary>
    public const byte InformationTypeMask = 0x01;
    /// <summary>The poll/final control bit.</summary>
    public const byte PollFinalMask = 0x10;
    /// <summary>The control bits excluding poll/final.</summary>
    public const byte ControlWithoutPollFinalMask = 0xef;
    /// <summary>The unnumbered-information control code without poll/final.</summary>
    public const byte UnnumberedInformationControl = 0x03;
    /// <summary>The modulo-eight sequence-number mask.</summary>
    public const byte SequenceMask = 0x07;
    /// <summary>The send-sequence offset within the control octet.</summary>
    public const int SendSequenceShift = 1;
    /// <summary>The receive-sequence offset within the control octet.</summary>
    public const int ReceiveSequenceShift = 5;
}
