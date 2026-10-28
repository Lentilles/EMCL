namespace EMCL.Framing.Hdlc.Frames;

/// <summary>Names HDLC control-field values used for link setup and stop-and-wait exchange.</summary>
public static class HdlcControl
{
    /// <summary>The SNRM command with the poll bit set.</summary>
    public const byte Snrm = 0x93;
    /// <summary>The UA response with the final bit set.</summary>
    public const byte Ua = 0x73;
    /// <summary>The DISC command with the poll bit set.</summary>
    public const byte Disc = 0x53;
    /// <summary>The DM response with the final bit set.</summary>
    public const byte Dm = 0x1f;
    /// <summary>The FRMR response with the final bit set.</summary>
    public const byte Frmr = 0x97;
    /// <summary>The RR supervisory-frame discriminator, excluding sequence and poll/final bits.</summary>
    public const byte ReceiveReady = 0x01;
    /// <summary>The RNR supervisory-frame discriminator, excluding sequence and poll/final bits.</summary>
    public const byte ReceiveNotReady = 0x05;
    /// <summary>The REJ supervisory-frame discriminator, excluding sequence and poll/final bits.</summary>
    public const byte Reject = 0x09;
    /// <summary>The mask selecting a supervisory-frame discriminator.</summary>
    public const byte SupervisoryTypeMask = 0x0f;

    /// <summary>Creates an RR command with the requested receive sequence and poll bit.</summary>
    /// <param name="receiveSequence">The next expected remote send sequence, modulo eight.</param>
    /// <returns>The complete RR control octet.</returns>
    public static byte CreateReceiveReady(byte receiveSequence)
    {
        if (receiveSequence > HdlcFrameFormat.SequenceMask)
        {
            throw new ArgumentOutOfRangeException(nameof(receiveSequence));
        }

        return (byte)(ReceiveReady | receiveSequence << HdlcFrameFormat.ReceiveSequenceShift
            | HdlcFrameFormat.PollFinalMask);
    }
}
