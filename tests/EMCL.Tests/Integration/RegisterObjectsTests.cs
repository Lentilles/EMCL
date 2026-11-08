using EMCL.Composition;
using EMCL.Core.Codecs;
using EMCL.Core.Objects;
using EMCL.Core.Operations;
using EMCL.Core.Values;
using EMCL.Messaging;
using EMCL.Objects.Registers;
using EMCL.Protocol.Apdu;

namespace EMCL.Tests.Integration;

public sealed class RegisterObjectsTests
{
    [Theory]
    [InlineData("C001C100040100010800FF0200", "C401C100060000002A", 4)]
    [InlineData("C301C100040100010800FF01010F00", "C701C10000", 4)]
    [InlineData("C301C100050100010800FF02010F00", "C701C10000", 5)]
    [InlineData("C001C100060100010800FF0200", "C401C1000100", 6)]
    public async Task ExecutesReferenceApdusAgainstNewHandlers(string requestHex, string responseHex, ushort classId)
    {
        var device = new EmclBuilder().Build("meter");
        var register = device.Add(classId, 0, LogicalName.Parse("1.0.1.8.0.255"));
        if (classId is 4 or 5)
        {
            register.State.Set(2, Number(DlmsType.UInt32, 42));
        }

        var codec = new ApduCodec();
        var request = Assert.IsType<LnRequest>(DecodeComplete(codec, FromHex(requestHex)));
        var response = await DlmsEndpoint.ExecuteAsync(request, device.Pipeline, device.LocalContext,
            cancellationToken: TestContext.Current.CancellationToken);
        AssertBytesEqual(FromHex(responseHex), codec.EncodeBytes(response));
        if (classId == 5)
        {
            Assert.Equal(42UL, ReadStoredValue(register, 3).AsUInt64());
            Assert.Equal(0UL, ReadStoredValue(register, 2).AsUInt64());
        }
    }

    [Fact]
    public async Task CapturesNewRegisterValuesAndTimestampsIntoProfileGeneric()
    {
        var device = new EmclBuilder().Build("meter");
        var extended = device.Add(4, 0, LogicalName.Parse("1.0.1.8.0.255"));
        var demand = device.Add(5, 0, LogicalName.Parse("1.0.1.6.0.255"));
        var buffer = device.Add(7, 1, LogicalName.Parse("1.0.99.1.0.255"));
        var extendedHandler = Assert.IsType<ExtendedRegisterHandler>(device.Handlers.Resolve("meter", extended));
        var demandHandler = Assert.IsType<DemandRegisterHandler>(device.Handlers.Resolve("meter", demand));
        Assert.Equal(CosemResultCode.Success, (await extendedHandler.CaptureAsync(extended, Number(DlmsType.UInt32, 123),
            cancellationToken: TestContext.Current.CancellationToken)).Error);
        await WriteValueAsync(device, demand, 8, Number(DlmsType.UInt32, 60));
        Assert.Equal(CosemResultCode.Success, (await demandHandler.RecordSampleAsync(demand, Number(DlmsType.UInt32, 10),
            TestContext.Current.CancellationToken)).Error);
        await WriteValueAsync(device, buffer, 3, DlmsValue.Array(CaptureDefinition(extended), CaptureDefinition(extended, 5),
            CaptureDefinition(demand), CaptureDefinition(demand, 7)));
        await WriteValueAsync(device, buffer, 8, Number(DlmsType.UInt32, 2));
        Assert.Equal(CosemResultCode.Success, (await ExecuteOperationAsync(device, buffer, CosemOperationKind.Invoke,
            2, Number(DlmsType.Int8, 0))).Error);
        var row = Assert.Single(ReadStoredValue(buffer, 2).Items).Items;
        Assert.Equal(123L, row[0].AsInt64());
        Assert.Same(ReadStoredValue(extended, 5), row[1]);
        Assert.Equal(10d, row[2].Value);
        Assert.Same(ReadStoredValue(demand, 7), row[3]);
    }
}
