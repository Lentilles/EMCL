using EMCL.Composition;
using EMCL.Core.Codecs;
using EMCL.Core.Objects;
using EMCL.Core.Operations;
using EMCL.Core.Values;
using EMCL.Objects.Handlers;
using EMCL.Protocol.Acse;
using EMCL.Protocol.Apdu;
using EMCL.Protocol.Axdr;

namespace EMCL.Tests.Integration;

public sealed class BoundaryRegressionTests
{
    [Fact]
    public async Task PreservesBoundaryValuesAndOperationAtomicity()
    {
        // A year whose low octet is FF is a valid year, not an unspecified year (FFFF).
        var dt = CosemDateTime.FromDateTimeOffset(new DateTimeOffset(2047, 1, 1, 0, 0, 0, TimeSpan.Zero));
        Assert.Equal(2047, dt.ToDateTimeOffset().Year);
        var acse = new AcseCodec();
        Assert.Equal(DecodeStatus.InvalidData, acse.Decode(new(FromHex("6000"))).Status);
        Assert.Equal(DecodeStatus.InvalidData, acse.Decode(new(FromHex("610BA109060760857405080101"))).Status);
        AssertBytesEqual(FromHex("6203800100"), acse.EncodeBytes(AcseApdu.Release()));
        var initiate = FromHex("0800065F1F0400007E1F04B00007");
        var aare = DecodeComplete(acse, acse.EncodeBytes(AcseApdu.Aare(initiate)));
        AssertBytesEqual(initiate, aare.UserInformation!.Value.AsSpan());
        var limited = new AxdrCodec(new()
        {
            MaxMessageBytes = 4
        });
        Assert.Throws<ArgumentException>(() => limited.EncodeBytes(DlmsValue.Octets([1, 2, 3, 4])));
        var d = new EmclBuilder().Build("d");
        var r = d.Add(3, 0, LogicalName.Parse("1.0.1.8.0.255"));
        r.State.Set(2, Number(DlmsType.UInt32, 8));
        var original = d.Handlers.Resolve("d", r);
        var custom = new ObjectHandlerOverrides(original).OnRead(2, (obj, op, ctx, ct) => ValueTask.FromResult(OperationResult.Ok(Number(DlmsType.UInt32, 91))));
        d.Handlers.ForInstance("d", r.LogicalName, custom);
        Assert.Equal(91L, (await ExecuteOperationAsync(d, r, CosemOperationKind.Read, 2)).Value!.AsInt64());
        Assert.Equal(CosemResultCode.Success, (await ExecuteOperationAsync(d, r, CosemOperationKind.Invoke, 1, Number(DlmsType.Int8, 0))).Error);
        Assert.Equal(0L, ReadStoredValue(r, 2).AsInt64());
        var p = d.Add(7, 1, LogicalName.Parse("1.0.99.1.0.255"));
        await WriteValueAsync(d, p, 3, DlmsValue.Array(CaptureDefinition(r)));
        await ExecuteOperationAsync(d, p, CosemOperationKind.Invoke, 2, Number(DlmsType.Int8, 0));
        Assert.Equal(CosemResultCode.TypeUnmatched, (await ExecuteOperationAsync(d, p, CosemOperationKind.Write, 5, Number(DlmsType.Enum, 3))).Error);
        Assert.Equal(1L, ReadStoredValue(p, 5).AsInt64());
        Assert.Equal(1L, ReadStoredValue(p, 7).AsInt64());
        // No partial capture row may be appended when a later source is unavailable.
        var missing = d.Add(1, 0, LogicalName.Parse("0.0.96.1.0.255"));
        await WriteValueAsync(d, p, 3, DlmsValue.Array(CaptureDefinition(r), CaptureDefinition(missing)));
        Assert.Equal(CosemResultCode.ObjectUnavailable, (await ExecuteOperationAsync(d, p, CosemOperationKind.Invoke, 2, Number(DlmsType.Int8, 0))).Error);
        Assert.Equal(0L, ReadStoredValue(p, 7).AsInt64());
        Assert.Equal(DecodeStatus.InvalidData, new ApduCodec(new()
        {
            MaxMessageBytes = 4
        }).Decode(new(FromHex("C001C100030100010800FF0200"))).Status);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await d.Pipeline.ExecuteAsync(new(CosemOperationKind.Read, 3, r.LogicalName, 2), d.LocalContext, cancellation.Token));
        DlmsValue nested = DlmsValue.Null;
        for (int depth = 0; depth < 65; depth++)
        {
            nested = DlmsValue.Array(nested);
        }

        Assert.Throws<ArgumentException>(() => new AxdrCodec().EncodeBytes(nested));
    }
}
