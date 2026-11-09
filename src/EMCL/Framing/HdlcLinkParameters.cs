using EMCL.Framing.Hdlc.Frames;
using EMCL.Framing.Hdlc.Llc;

namespace EMCL.Framing;

/// <summary>HDLC information-field limits in bytes (including LLC) and window sizes in frames.</summary>
/// <param name="MaximumTransmitInformationBytes">Maximum information bytes sent by the parameter owner.</param>
/// <param name="MaximumReceiveInformationBytes">Maximum information bytes received by the parameter owner.</param>
/// <param name="TransmitWindowFrames">Maximum outstanding transmitted frames; the channel supports only one.</param>
/// <param name="ReceiveWindowFrames">Maximum outstanding frames received from the peer, from one through seven.</param>
public sealed record HdlcLinkParameters(
    int MaximumTransmitInformationBytes = HdlcFrameFormat.DefaultInformationLength,
    int MaximumReceiveInformationBytes = HdlcFrameFormat.DefaultInformationLength,
    int TransmitWindowFrames = 1, int ReceiveWindowFrames = 1)
{
    internal void ValidateProposal()
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(MaximumTransmitInformationBytes, LlcHeader.Length);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(MaximumTransmitInformationBytes, HdlcNegotiation.MaximumInformationBytes);
        ArgumentOutOfRangeException.ThrowIfLessThan(MaximumReceiveInformationBytes, LlcHeader.Length);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(MaximumReceiveInformationBytes, HdlcNegotiation.MaximumInformationBytes);
        if (TransmitWindowFrames != 1)
        {
            throw new ArgumentOutOfRangeException(nameof(TransmitWindowFrames), "HDLC stop-and-wait requires a one-frame transmit window.");
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(ReceiveWindowFrames, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(ReceiveWindowFrames, HdlcFrameFormat.SequenceMask);
    }
}
