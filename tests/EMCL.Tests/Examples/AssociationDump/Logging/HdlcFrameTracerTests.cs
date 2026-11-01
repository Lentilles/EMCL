using EMCL.Core.Codecs;
using EMCL.Examples.AssociationDump.Logging;
using EMCL.Framing;
using EMCL.Framing.Hdlc.Frames;
using EMCL.Protocol.Apdu;

namespace EMCL.Tests.Examples.AssociationDump.Logging;

public sealed class HdlcFrameTracerTests
{
    [Fact]
    public void LogsCompleteAarqFrameDuringAuthenticatedAssociation()
    {
        using var output = new StringWriter();
        var tracer = new HdlcFrameTracer(output, redactAuthenticationActions: true);
        var local = HdlcAddress.Client(48);
        var remote = HdlcAddress.Server(1, 17);
        var adapter = new HdlcApduAdapter(local, remote);
        var frame = adapter.Frame(new byte[] { 0x60, 0x02, 0x01, 0x02 }).Single();
        string expectedBytes = Convert.ToHexString(new HdlcCodec().EncodeBytes(frame));

        tracer.Log(frame, received: false);

        Assert.Equal($"[0001] TX I N(S)=0 N(R)=0 ({expectedBytes.Length / 2} bytes): {expectedBytes}",
            output.ToString().TrimEnd());
        Assert.DoesNotContain("[redacted", output.ToString());
    }

    [Fact]
    public void AuthenticationActionFrameRemainsRedacted()
    {
        using var output = new StringWriter();
        var tracer = new HdlcFrameTracer(output, redactAuthenticationActions: true);
        var frame = new HdlcApduAdapter(HdlcAddress.Client(48), HdlcAddress.Server(1, 17))
            .Frame(new byte[] { (byte)ApduTag.ActionRequest, 0x01, 0x02 }).Single();

        tracer.Log(frame, received: false);

        Assert.Contains("[redacted: authentication data]", output.ToString());
    }
}
