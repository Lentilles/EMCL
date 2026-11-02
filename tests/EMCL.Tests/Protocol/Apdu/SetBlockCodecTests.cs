using EMCL.Core.Codecs;
using EMCL.Core.Operations;
using EMCL.Protocol.Apdu;

namespace EMCL.Tests.Protocol.Apdu;

public sealed class SetBlockCodecTests
{
    [Theory]
    [InlineData("C102C100030100010800FF0200000000000103090401")]
    [InlineData("C102C100030100010800FF02010112002A000000000103090401")]
    [InlineData("C103C1010000000203020304")]
    [InlineData("C502C100000001")]
    [InlineData("C503C10000000002")]
    [InlineData("C503C10300000001")]
    public void EncodesIndependentVectorsAndRequiresEveryPrefixOctet(string hex)
    {
        var codec = new ApduCodec();
        byte[] wire = FromHex(hex);
        for (int count = 0; count < wire.Length; count++)
        {
            Assert.Equal(DecodeStatus.NeedMoreData, codec.Decode(new(wire.AsMemory(0, count))).Status);
        }

        var decoded = codec.Decode(new(wire));
        Assert.Equal(DecodeStatus.Done, decoded.Status);
        Assert.Equal(wire.Length, decoded.Consumed);
        AssertBytesEqual(wire, codec.EncodeBytes(decoded.Value!));
        Assert.Equal(wire.Length, codec.Decode(new(wire.Concat(wire).ToArray())).Consumed);
    }

    [Fact]
    public void PreservesFieldsAndSeparatesAcknowledgementFromFinalResult()
    {
        var codec = new ApduCodec();
        var first = Assert.IsType<SetFirstBlockRequest>(DecodeComplete(codec, FromHex("C102C100030100010800FF0200000000000103090401")));
        Assert.Equal((byte)0xC1, first.InvokeIdAndPriority);
        Assert.Equal(CosemOperationKind.Write, first.Operation.Kind);
        Assert.Equal((ushort)3, first.Operation.ClassId);
        Assert.Equal("1.0.1.8.0.255", first.Operation.LogicalName.ToString());
        Assert.Equal((sbyte)2, first.Operation.Index);
        Assert.Null(first.Operation.Value);
        Assert.False(first.LastBlock);
        Assert.Equal(1u, first.BlockNumber);
        AssertBytesEqual(FromHex("090401"), first.Data.AsSpan());
        var ack = Assert.IsType<SetBlockResponse>(DecodeComplete(codec, FromHex("C502C100000001")));
        Assert.Null(ack.Result);
        var final = Assert.IsType<SetBlockResponse>(DecodeComplete(codec, FromHex("C503C10000000002")));
        Assert.Equal(CosemError.Success, final.Result);
        Assert.Equal(2u, final.BlockNumber);
    }

    [Theory]
    [InlineData("C103C1020000000100")]
    [InlineData("C103C10000000001820100")]
    [InlineData("C103C1000000000180")]
    public void RejectsInvalidBooleanAndLengths(string hex)
    {
        Assert.Equal(DecodeStatus.InvalidData, new ApduCodec(new()
        {
            MaxMessageBytes = 64
        }).Decode(new(FromHex(hex))).Status);
    }

    [Fact]
    public void BoundsEncodingOfRawFragments()
    {
        var codec = new ApduCodec(new()
        {
            MaxMessageBytes = 16
        });
        Assert.Throws<ArgumentException>(() => codec.EncodeBytes(new SetBlockRequest(0x40, true, 1, new byte[8].ToImmutableArray())));
    }
}
