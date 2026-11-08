using EMCL.Composition;
using EMCL.Core.Objects;
using EMCL.Core.Operations;
using EMCL.Core.Values;

namespace EMCL.Tests.Objects.ProfileGeneric;

public sealed class ProfileGenericTests
{
    [Fact]
    public async Task CapturesSelectsAndResetsBuffers()
    {
        var d = new EmclBuilder().Build("d");
        var r = d.Add(3, 0, LogicalName.Parse("1.0.1.8.0.255"));
        var p = d.Add(7, 1, LogicalName.Parse("1.0.99.1.0.255"));
        var definition = CaptureDefinition(r);
        await WriteValueAsync(d, p, 3, DlmsValue.Array(definition));
        await WriteValueAsync(d, p, 8, Number(DlmsType.UInt32, 3));
        foreach (int n in new[]
        {
            1,
            2,
            3,
            4
        }

        )
        {
            r.State.Set(2, Number(DlmsType.UInt32, n));
            Assert.Equal(CosemResultCode.Success, (await ExecuteOperationAsync(d, p, CosemOperationKind.Invoke, 2, Number(DlmsType.Int8, 0))).Error);
        }

        Assert.Equal(3, ReadStoredValue(p, 2).Items.Length);
        Assert.Equal(2L, ReadStoredValue(p, 2).Items[0].Items[0].AsInt64());
        Assert.Equal(3L, ReadStoredValue(p, 7).AsInt64());
        var entries = DlmsValue.Structure(Number(DlmsType.UInt32, 2), Number(DlmsType.UInt32, 0), Number(DlmsType.UInt16, 1), Number(DlmsType.UInt16, 0));
        var selected = (await ExecuteOperationAsync(d, p, CosemOperationKind.Read, 2, selection: new(2, entries))).Value!;
        Assert.Equal(2, selected.Items.Length);
        Assert.Equal(3L, selected.Items[0].Items[0].AsInt64());
        var range = DlmsValue.Structure(definition, Number(DlmsType.UInt32, 2), Number(DlmsType.UInt32, 3), DlmsValue.Array());
        Assert.Equal(2, (await ExecuteOperationAsync(d, p, CosemOperationKind.Read, 2, selection: new(1, range))).Value!.Items.Length);
        var desc = new CompactTypeDescription(DlmsType.Structure, [CompactTypeDescription.Scalar(DlmsType.UInt32)]);
        var compact = new DlmsValue(DlmsType.CompactArray, new DlmsCompactArray(desc, [DlmsValue.Structure(Number(DlmsType.UInt32, 8)), DlmsValue.Structure(Number(DlmsType.UInt32, 9))]));
        await WriteValueAsync(d, p, 2, compact);
        Assert.Equal(DlmsType.CompactArray, ReadStoredValue(p, 2).Type);
        Assert.Equal(2L, ReadStoredValue(p, 7).AsInt64());
        Assert.Single((await ExecuteOperationAsync(d, p, CosemOperationKind.Read, 2, selection: new(2, entries))).Value!.Items);
        await WriteValueAsync(d, p, 3, DlmsValue.Array(definition));
        Assert.Equal(0L, ReadStoredValue(p, 7).AsInt64());
        r.State.Set(2, Number(DlmsType.UInt32, 7));
        await ExecuteOperationAsync(d, p, CosemOperationKind.Invoke, 2, Number(DlmsType.Int8, 0));
        await ExecuteOperationAsync(d, p, CosemOperationKind.Invoke, 1, Number(DlmsType.Int8, 0));
        Assert.Equal(0L, ReadStoredValue(p, 7).AsInt64());
    }
}
