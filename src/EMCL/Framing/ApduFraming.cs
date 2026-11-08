using EMCL.Framing.Hdlc.Frames;

namespace EMCL.Framing;

/// <summary>Configures how a byte transport carries complete application PDUs.</summary>
public abstract record ApduFraming
{
    private protected ApduFraming() { }

    /// <summary>Uses DLMS Wrapper framing with the supplied wPorts.</summary>
    /// <param name="LocalPort">The local Wrapper wPort.</param>
    /// <param name="RemotePort">The remote Wrapper wPort.</param>
    public sealed record Wrapper(ushort LocalPort, ushort RemotePort) : ApduFraming;

    /// <summary>Uses an HDLC client link with the supplied addresses and setup timeout.</summary>
    /// <param name="Local">The local HDLC client address.</param>
    /// <param name="Remote">The remote HDLC server address.</param>
    /// <param name="LinkTimeout">Maximum time allowed for SNRM/UA setup.</param>
    /// <param name="FrameObserver">Optional callback for transmitted (false) and received (true) frames.</param>
    public sealed record Hdlc(HdlcAddress Local, HdlcAddress Remote, TimeSpan LinkTimeout,
        Action<HdlcFrame, bool>? FrameObserver = null) : ApduFraming
    {
        /// <summary>Allows incoming frames with a valid FCS to omit the closing flag. Defaults to false.</summary>
        public bool AllowMissingClosingFlag
        {
            get; init;
        }
    }
}
