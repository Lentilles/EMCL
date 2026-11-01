using EMCL.Core.Codecs;
using EMCL.Framing.Hdlc.Frames;
using EMCL.Framing.Hdlc.Segmentation;

namespace EMCL.Tests.Framing;

public sealed class HdlcCodecTests
{
    [Fact]
    public void ExtendedServerAddressEncodesObservedSnrm()
    {
        var frame = new HdlcFrame(HdlcAddress.Server(1, 17, extended: true),
            HdlcAddress.Client(48), HdlcControl.Snrm, []);
        var codec = new HdlcCodec();

        Assert.Equal("7EA00A0002002361937E377E", Convert.ToHexString(codec.EncodeBytes(frame)));
    }

    [Fact]
    public void ObservedUaFramesHaveSameLogicalAndPhysicalServerAddresses()
    {
        var codec = new HdlcCodec();
        var fourByteUa = DecodeComplete(codec, FromHex("7EA00A6100020023739DE57E"));
        var twoByteUa = DecodeComplete(codec, FromHex("7EA00861022373CD557E"));

        Assert.Equal(HdlcControl.Ua, fourByteUa.Control);
        Assert.Equal(HdlcControl.Ua, twoByteUa.Control);
        Assert.Equal(HdlcAddress.Client(48), fourByteUa.Destination);
        Assert.Equal(fourByteUa.Destination, twoByteUa.Destination);
        Assert.Equal(HdlcAddress.Server(1, 17, extended: true), fourByteUa.Source);
        Assert.Equal(HdlcAddress.Server(1, 17), twoByteUa.Source);
        Assert.True(fourByteUa.Source.HasSameServerComponents(twoByteUa.Source));
        Assert.Empty(fourByteUa.Information);
        Assert.Empty(twoByteUa.Information);
    }

    [Fact]
    public void ValidAareFrameHeaderDecodesAndBadClosingFlagReportsBoundary()
    {
        var codec = new HdlcCodec();
        var information = new byte[79];
        new byte[] { 0xe6, 0xe7, 0, 0x61, 0x4a }.CopyTo(information, 0);
        byte[] frame = codec.EncodeBytes(new(HdlcAddress.Client(48), HdlcAddress.Server(1, 17),
            0x30, information.ToImmutableArray()));

        Assert.StartsWith("7EA05961022330745BE6E700614A", Convert.ToHexString(frame));
        Assert.Equal(DecodeStatus.Done, codec.Decode(new(frame)).Status);

        frame[^1] = 0;
        var malformed = codec.Decode(new(frame));
        Assert.Equal(DecodeStatus.InvalidData, malformed.Status);
        Assert.Contains("declared length 89", malformed.Error);
        Assert.Contains("offset 90", malformed.Error);
        Assert.Contains("found 00", malformed.Error);
    }

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
