using EMCL.Core.Codecs;
using EMCL.Protocol.Apdu;

namespace EMCL.Tests.Protocol.Apdu;

public sealed class ApduCodecReferenceTests
{
    [Theory]
    [InlineData("C001C100030100010800FF0200")]
    [InlineData("C401C1000600003039")]
    [InlineData("C401C10103")]
    [InlineData("C101C100030100010800FF0200060000002A")]
    [InlineData("C501C100")]
    [InlineData("C301C100030100010800FF01010F00")]
    [InlineData("C701C10000")]
    [InlineData("C701C100010012002A")]
    [InlineData("C701C100010103")]
    [InlineData("C002C100000001")]
    [InlineData("C402C100000000010003010203")]
    [InlineData("C402C1010000000200020405")]
    [InlineData("C402C101000000010104")]
    [InlineData("0F0000000100112A")]
    [InlineData("01000000065F1F0400007E1F04B0")]
    [InlineData("0800065F1F0400007E1F04B00007")]
    [InlineData("C001C100070100630100FF020102020406000000010600000000120001120000")]
    public void EncodesAndDecodesReferenceVector(string hex)
    {
        var codec = new ApduCodec();
        byte[] wire = FromHex(hex);
        AssertBytesEqual(wire, codec.EncodeBytes(DecodeComplete(codec, wire)));
    }
}
