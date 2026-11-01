using EMCL.Core.Codecs;
using EMCL.Framing.Hdlc.Frames;
using EMCL.Framing.Hdlc.Llc;
using EMCL.Protocol.Apdu;

namespace EMCL.Examples.AssociationDump.Logging;

internal sealed class HdlcFrameTracer(TextWriter output, bool redactAuthenticationActions)
{
    private readonly object traceLock = new();
    private readonly HdlcCodec codec = new();
    private int messageCount;

    internal void Log(HdlcFrame frame, bool received)
    {
        string kind = frame.Control switch
        {
            HdlcControl.Snrm => "SNRM",
            HdlcControl.Ua => "UA",
            HdlcControl.Dm => "DM",
            HdlcControl.Disc => "DISC",
            _ when frame.IsInformation => $"I N(S)={frame.SendSequence} N(R)={frame.ReceiveSequence}",
            _ when (frame.Control & HdlcControl.SupervisoryTypeMask) == HdlcControl.ReceiveReady
                => $"RR N(R)={frame.ReceiveSequence}",
            _ => $"control={frame.Control:X2}"
        };
        bool secret = redactAuthenticationActions && frame.IsInformation && frame.Information.Length > LlcHeader.Length
            && frame.Information[LlcHeader.Length] is (byte)ApduTag.ActionRequest or (byte)ApduTag.ActionResponse;
        byte[] bytes = codec.EncodeBytes(frame);
        string contents = secret ? "[redacted: authentication data]" : Convert.ToHexString(bytes);
        lock (traceLock)
        {
            output.WriteLine($"[{++messageCount:D4}] {(received ? "RX" : "TX")} {kind} ({bytes.Length} bytes): {contents}");
        }
    }
}
