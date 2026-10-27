using EMCL.Core.Codecs;
using EMCL.Framing.Hdlc.Frames;
using EMCL.Framing.Hdlc.Segmentation;

namespace EMCL.Tests.Framing;

public sealed class HdlcCodecTests
{
    [Fact]
    public void ValidatesAddressesChecksumsAndSegmentation()
    {
        Assert.Equal((ushort)0x906e, HdlcChecksum.Compute(Encoding.ASCII.GetBytes("123456789")));
        var c = new HdlcCodec();
        byte[] snrm = FromHex("7EA0070321930F017E");
        var f = DecodeComplete(c, snrm);
        Assert.Equal((byte)0x93, f.Control);
        AssertBytesEqual(snrm, c.EncodeBytes(f));
        foreach (var server in new[]
        {
            HdlcAddress.Server(1),
            HdlcAddress.Server(1, 2),
            HdlcAddress.Server(255, 8000, true)
        }

        )
        {
            byte[] bytes = c.EncodeBytes(new(server, HdlcAddress.Client(16), 0x10, [0xe6, 0xe6, 0, 0x7e, 0]));
            Assert.Equal(server, DecodeComplete(c, bytes).Destination);
            AssertBytesEqual(bytes, c.EncodeBytes(DecodeComplete(c, bytes)));
            bytes[^3] ^= 1;
            Assert.Equal(DecodeStatus.InvalidData, c.Decode(new(bytes)).Status);
        }

        var pair = snrm.Concat(snrm.Skip(1)).ToArray();
        var first = c.Decode(new(pair));
        Assert.Equal(DecodeStatus.Done, c.Decode(new(pair.AsMemory((int)first.Consumed))).Status);
        byte[] info = c.EncodeBytes(new(HdlcAddress.Server(1), HdlcAddress.Client(16), 0x10, [1, 2, 3]));
        info[6] ^= 1;
        Assert.Equal(DecodeStatus.InvalidData, c.Decode(new(info)).Status);
        var assembler = new HdlcSegmentAssembler(new()
        {
            MaxMessageBytes = 6,
            MaxBlockCount = 2
        });
        HdlcFrame F(byte ns, bool more, params byte[] data) => new(HdlcAddress.Client(16), HdlcAddress.Server(1), (byte)(ns << 1), data.ToImmutableArray(), more);
        Assert.True(assembler.Accept(F(7, true, 1, 2)) == null);
        AssertBytesEqual([1, 2, 3], assembler.Accept(F(0, false, 3))!.Value.AsSpan());
        Assert.True(!assembler.HasPending);
        assembler.Accept(F(0, true, 1));
        Assert.Throws<InvalidDataException>(() => assembler.Accept(F(2, false, 2)));
        Assert.True(!assembler.HasPending);
        assembler.Accept(F(0, true, 1));
        Assert.Throws<InvalidDataException>(() => assembler.Accept(F(1, false, 2) with { Source = HdlcAddress.Server(2) }));
        assembler.Accept(F(0, true, 1, 2, 3));
        Assert.Throws<InvalidDataException>(() => assembler.Accept(F(1, false, 4, 5, 6, 7)));
    }
}
