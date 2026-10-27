using EMCL.Core.Codecs;
using EMCL.Core.Values;
using EMCL.Protocol.Axdr;

namespace EMCL.Tests.Protocol.Axdr;

public sealed class CompactArrayTests
{
    [Fact]
    public void PreservesNestedRowsAndRejectsMalformedContents()
    {
        var c = new AxdrCodec();
        Assert.Equal(2, ((DlmsCompactArray)DecodeComplete(c, FromHex("13020211090701014102024243")).Value!).Rows.Length);
        Assert.Equal(DecodeStatus.InvalidData, c.Decode(new(FromHex("13120101"))).Status);
    }
}
