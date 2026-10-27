using EMCL.Core.Codecs;
using EMCL.Core.Values;
using EMCL.Protocol.Acse;
using EMCL.Protocol.Apdu;

namespace EMCL.Tests.Protocol;

public sealed class ProtocolBehaviorTests
{
    [Fact]
    public void HandlesAssociationAndLnMessageFields()
    {
        var a = new AcseCodec();
        byte[] initiate = FromHex("01000000065F1F0400007E1F04B0");
        AssertBytesEqual(FromHex("601DA109060760857405080101BE10040E01000000065F1F0400007E1F04B0"), a.EncodeBytes(AcseApdu.Aarq(initiate)));
        Assert.True(DecodeComplete(a, a.EncodeBytes(AcseApdu.Aarq(initiate, authenticationMechanism: 1, authenticationValue: Encoding.ASCII.GetBytes("secret")))).Find(0xac) != null);
        foreach (string bad in new[]
        {
            "6003A10500",
            "6001A1",
            "6003A10106",
            "6080"
        }

        )
        {
            Assert.Equal(DecodeStatus.InvalidData, a.Decode(new(FromHex(bad))).Status);
        }

        var c = new ApduCodec();
        Assert.Equal(12345UL, ((LnResponse)DecodeComplete(c, FromHex("C401C1000600003039"))).Result.Value!.AsUInt64());
        Assert.Equal(DecodeStatus.Unsupported, c.Decode(new(FromHex("C003C1"))).Status);
        Assert.Equal(DecodeStatus.InvalidData, c.Decode(new(FromHex("C001C100030100010800FF0202"))).Status);
        var n = new DataNotification(1, FromHex("07EA0A1A010C223800FF4C00").ToImmutableArray(), DlmsValue.Array(Number(DlmsType.UInt8, 7)));
        AssertBytesEqual(c.EncodeBytes(n), c.EncodeBytes(DecodeComplete(c, c.EncodeBytes(n))));
    }
}
