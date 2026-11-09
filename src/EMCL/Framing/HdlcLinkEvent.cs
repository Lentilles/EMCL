namespace EMCL.Framing;

/// <summary>Reports HDLC frame rejection and automatic recovery independently of the frame trace.</summary>
public abstract record HdlcLinkEvent
{
    private protected HdlcLinkEvent() { }

    /// <summary>A frame was discarded after a checksum failure.</summary>
    /// <param name="Error">The checksum diagnostic.</param>
    public sealed record ChecksumError(string Error) : HdlcLinkEvent;

    /// <summary>The acknowledgement timer expired and the channel polled the peer with RR.</summary>
    /// <param name="SendSequence">The send sequence of the outstanding I-frame.</param>
    /// <param name="Attempt">The one-based recovery attempt number.</param>
    public sealed record RecoveryPoll(byte SendSequence, int Attempt) : HdlcLinkEvent;

    /// <summary>The peer's response to a recovery poll showed that it still expected the outstanding I-frame.</summary>
    /// <param name="SendSequence">The send sequence of the retransmitted I-frame.</param>
    /// <param name="Attempt">The one-based recovery attempt number.</param>
    public sealed record Retransmission(byte SendSequence, int Attempt) : HdlcLinkEvent;
}
