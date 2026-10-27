using EMCL.Core.Operations;
using EMCL.Protocol.BlockTransfer;

namespace EMCL.Tests.Protocol.BlockTransfer;

public sealed class GetBlockAssemblerTests
{
    [Fact]
    public void IsolatesTransfersAndRejectsInvalidBlocks()
    {
        var c = new GetBlockAssembler(new()
        {
            MaxMessageBytes = 8,
            MaxBlockCount = 2
        });
        var a = new GetExchangeKey("a", "d", "assoc", 1);
        var b = a with
        {
            ChannelId = "b"
        };
        c.Begin(a);
        c.Begin(b);
        Assert.True(!c.Accept(a, new(0xc1, false, 1, [1, 2])).Complete);
        Assert.True(c.Accept(b, new(0xc1, true, 1, [7])).Complete);
        var done = c.Accept(a, new(0xc1, true, 2, [3]));
        Assert.True(done.Complete);
        AssertBytesEqual([1, 2, 3], done.Data.AsSpan());
        Assert.Equal(CosemError.NoLongGetInProgress, c.Accept(a, new(0xc1, true, 2, [3])).Error);
        c.Begin(a);
        Assert.Equal(CosemError.DataBlockNumberInvalid, c.Accept(a, new(0xc1, true, 2, [])).Error);
        c.Begin(a);
        c.Accept(a, new(0xc1, false, 1, [1]));
        Assert.Equal(CosemError.DataBlockNumberInvalid, c.Accept(a, new(0xc1, false, 1, [1])).Error);
        c.Begin(a);
        Assert.Equal(CosemError.LongGetAborted, c.Accept(a, new(0xc1, true, 1, [1, 2, 3, 4, 5, 6, 7, 8, 9])).Error);
        c.Begin(a);
        Assert.Equal(CosemError.ObjectUnavailable, c.Accept(a, new(0xc1, true, 1, [], CosemError.ObjectUnavailable)).Error);
        c.Begin(a);
        c.Accept(a, new(0xc1, false, 1, []));
        c.Accept(a, new(0xc1, false, 2, []));
        Assert.Equal(CosemError.LongGetAborted, c.Accept(a, new(0xc1, true, 3, [])).Error);
        c.Begin(a);
        c.Abort(a);
        Assert.Equal(CosemError.NoLongGetInProgress, c.Accept(a, new(0xc1, true, 1, [])).Error);
    }
}
