using EMCL.Core.Codecs;
using EMCL.Protocol.Acse;

namespace EMCL.Tests.Protocol.Acse;

public sealed class AcseCodecReferenceTests
{
    [Theory]
    [InlineData("601DA109060760857405080101BE10040E01000000065F1F0400007E1F04B0")]
    [InlineData("6129A109060760857405080101A203020100A305A103020100BE10040E0800065F1F0400007E1F04B00007")]
    [InlineData("6203800100")]
    [InlineData("6303800100")]
    [InlineData("6200")]
    public void EncodesAndDecodesReferenceVector(string hex)
    {
        var codec = new AcseCodec();
        byte[] wire = FromHex(hex);
        AssertBytesEqual(wire, codec.EncodeBytes(DecodeComplete(codec, wire)));
    }
}
