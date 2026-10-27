using EMCL.Core.Codecs;
using EMCL.Core.Values;
using EMCL.Protocol.Axdr;

namespace EMCL.Tests.Protocol.Axdr;

public sealed class AxdrCodecTests
{
    [Fact]
    public void PreservesValuesAndEnforcesLimits()
    {
        var c = new AxdrCodec();
        var pair = DecodeComplete(c, FromHex("02020FFF1601"));
        Assert.Equal(-1L, pair.Items[0].AsInt64());
        Assert.Equal(DlmsType.Enum, pair.Items[1].Type);
        var dt = CosemDateTime.FromValue(DecodeComplete(c, FromHex("1907EA0A1A010C223800FF4C00")));
        Assert.Equal((short)-180, dt.Deviation);
        Assert.Equal(TimeSpan.FromHours(3), dt.ToDateTimeOffset().Offset);
        Assert.Throws<InvalidOperationException>(() => new CosemDateTime(FromHex("FFFFFFFFFFFFFFFFFF8000FF")).ToDateTimeOffset());
        var big = FromHex("098180").Concat(new byte[128]).ToArray();
        AssertBytesEqual(big, c.EncodeBytes(DecodeComplete(c, big)));
        foreach (var invalid in new[]
        {
            "0302",
            "0980",
            "0C01FF"
        }

        )
        {
            Assert.Equal(DecodeStatus.InvalidData, c.Decode(new(FromHex(invalid))).Status);
        }

        Assert.Equal(DecodeStatus.Unsupported, c.Decode(new(FromHex("07"))).Status);
        var small = new AxdrCodec(new()
        {
            MaxDepth = 2,
            MaxCollectionItems = 3,
            MaxMessageBytes = 32
        });
        foreach (var invalid in new[]
        {
            "010101010101010100",
            "010400000000",
            "0921"
        }

        )
        {
            Assert.Equal(DecodeStatus.InvalidData, small.Decode(new(FromHex(invalid))).Status);
        }

        Assert.Throws<OverflowException>(() => c.EncodeBytes(Number(DlmsType.UInt8, 256)));
        Assert.Throws<ArgumentException>(() => c.EncodeBytes(new(DlmsType.VisibleString, "я")));
        Assert.Equal(2L, c.Decode(new(FromHex("11011602"))).Consumed);
    }
}
