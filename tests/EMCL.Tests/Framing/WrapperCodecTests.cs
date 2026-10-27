using EMCL.Core.Codecs;
using EMCL.Framing.Wrapper;

namespace EMCL.Tests.Framing;

public sealed class WrapperCodecTests
{
    [Fact]
    public void ChecksHeaderAndPayloadBoundaries()
    {
        var c = new WrapperCodec();
        var wire = FromHex("000100100001000DC001C100030100010800FF0200");
        var f = DecodeComplete(c, wire);
        Assert.Equal((ushort)16, f.SourcePort);
        Assert.Equal((ushort)1, f.DestinationPort);
        AssertBytesEqual(wire, c.EncodeBytes(f));
        Assert.Equal((long)wire.Length, c.Decode(new(wire.Concat(wire).ToArray())).Consumed);
        Assert.Equal(DecodeStatus.Unsupported, c.Decode(new(FromHex("0002001000010000"))).Status);
        Assert.Equal(DecodeStatus.InvalidData, new WrapperCodec(new()
        {
            MaxMessageBytes = 4
        }).Decode(new(wire)).Status);
    }
}
