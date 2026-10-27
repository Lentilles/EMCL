using EMCL.Framing.Hdlc.Frames;
using EMCL.Framing.Wrapper;
using EMCL.Protocol.Acse;
using EMCL.Protocol.Apdu;
using EMCL.Protocol.Axdr;

namespace EMCL.Tests.Protocol;

public sealed class DecoderRobustnessTests
{
    [Fact]
    public void ReturnsStatusesForMalformedInput()
    {
        var random = new Random(42);
        var axdr = new AxdrCodec(new()
        {
            MaxCollectionItems = 100,
            MaxMessageBytes = 256,
            MaxDepth = 8
        });
        var apdu = new ApduCodec(new()
        {
            MaxCollectionItems = 100,
            MaxMessageBytes = 256,
            MaxDepth = 8
        });
        var acse = new AcseCodec();
        var hdlc = new HdlcCodec();
        var wrapper = new WrapperCodec();
        for (int i = 0; i < 3000; i++)
        {
            var bytes = new byte[random.Next(0, 128)];
            random.NextBytes(bytes);
            var input = new ReadOnlySequence<byte>(bytes);
            _ = axdr.Decode(input);
            _ = apdu.Decode(input);
            _ = acse.Decode(input);
            _ = hdlc.Decode(input);
            _ = wrapper.Decode(input);
        }
    }
}
