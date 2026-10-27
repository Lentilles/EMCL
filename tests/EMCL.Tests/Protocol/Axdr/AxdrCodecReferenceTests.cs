using EMCL.Core.Codecs;
using EMCL.Protocol.Axdr;

namespace EMCL.Tests.Protocol.Axdr;

public sealed class AxdrCodecReferenceTests
{
    [Theory]
    [InlineData("00")]
    [InlineData("0301")]
    [InlineData("02020FFF1601")]
    [InlineData("04090080")]
    [InlineData("05FFFFFFFE")]
    [InlineData("06FFFFFFFF")]
    [InlineData("09030001FF")]
    [InlineData("0A03414243")]
    [InlineData("0C02D18F")]
    [InlineData("0D12")]
    [InlineData("10FF80")]
    [InlineData("1100")]
    [InlineData("12FFFF")]
    [InlineData("14FFFFFFFFFFFFFFFE")]
    [InlineData("15FFFFFFFFFFFFFFFF")]
    [InlineData("173F800000")]
    [InlineData("183FF0000000000000")]
    [InlineData("1907EA0A1A010C223800FF4C00")]
    [InlineData("1A07EA0A1A01")]
    [InlineData("1B0C223800")]
    [InlineData("1C80")]
    [InlineData("1D8000")]
    [InlineData("1E80000000")]
    [InlineData("1FFF")]
    [InlineData("20FFFF")]
    [InlineData("21FFFFFFFF")]
    [InlineData("010211011102")]
    [InlineData("0C0CD09FD180D0B8D0B2D0B5D182")]
    [InlineData("13020211090701014102024243")]
    [InlineData("13010002110401020304")]
    [InlineData("1302020012020001")]
    public void EncodesAndDecodesReferenceVector(string hex)
    {
        var codec = new AxdrCodec();
        byte[] wire = FromHex(hex);
        AssertBytesEqual(wire, codec.EncodeBytes(DecodeComplete(codec, wire)));
    }
}
