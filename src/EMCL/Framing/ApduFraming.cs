using EMCL.Framing.Hdlc.Frames;

namespace EMCL.Framing;

/// <summary>Configures how a byte transport carries complete application PDUs.</summary>
public abstract record ApduFraming
{
    private protected ApduFraming() { }

    /// <summary>Uses DLMS Wrapper framing with the supplied wPorts.</summary>
    /// <param name="LocalPort">The local Wrapper wPort.</param>
    /// <param name="RemotePort">The remote Wrapper wPort.</param>
    public sealed record Wrapper(ushort LocalPort, ushort RemotePort) : ApduFraming
    {
        /// <summary>Gets an optional additional local wPort for incoming DataNotification messages.</summary>
        public ushort? NotificationPort { get; init; }
    }

    /// <summary>Uses an HDLC client link with the supplied addresses and setup timeout.</summary>
    /// <param name="Local">The local HDLC client address.</param>
    /// <param name="Remote">The remote HDLC server address.</param>
    /// <param name="LinkTimeout">Maximum time allowed for SNRM/UA setup.</param>
    /// <param name="FrameObserver">Optional callback for transmitted (false) and received (true) frames.</param>
    public sealed record Hdlc(HdlcAddress Local, HdlcAddress Remote, TimeSpan LinkTimeout,
        Action<HdlcFrame, bool>? FrameObserver = null) : ApduFraming
    {
        /// <summary>Gets an optional additional client address for incoming UI frames carrying DataNotification.</summary>
        public HdlcAddress? NotificationAddress { get; init; }

        /// <summary>Allows incoming frames with a valid FCS to omit the closing flag. Defaults to false.</summary>
        public bool AllowMissingClosingFlag
        {
            get; init;
        }

        /// <summary>Maximum recovery attempts per unacknowledged I-frame. Zero disables automatic recovery.</summary>
        public int InformationFrameRetryCount { get; init; }

        /// <summary>Time to wait for acknowledgement before polling the peer or reporting exhausted recovery.</summary>
        public TimeSpan AcknowledgementTimeout { get; init; } = TimeSpan.FromSeconds(1);

        /// <summary>Optional observer of discarded frames and recovery attempts.</summary>
        public Action<HdlcLinkEvent>? LinkObserver { get; init; }

        internal void Validate()
        {
            if (LinkTimeout <= TimeSpan.Zero || LinkTimeout.TotalMilliseconds > uint.MaxValue - 1)
            {
                throw new ArgumentOutOfRangeException(nameof(LinkTimeout));
            }

            ArgumentOutOfRangeException.ThrowIfNegative(InformationFrameRetryCount);
            if (AcknowledgementTimeout <= TimeSpan.Zero || AcknowledgementTimeout.TotalMilliseconds > uint.MaxValue - 1)
            {
                throw new ArgumentOutOfRangeException(nameof(AcknowledgementTimeout));
            }
        }
    }
}
