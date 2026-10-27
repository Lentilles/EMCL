using EMCL.Composition;
using EMCL.Core.Objects;
using EMCL.Core.Operations;
using EMCL.Core.Values;
using EMCL.Profiles.BlueBook;

namespace EMCL.Tests.Objects.Handlers;

public sealed class RegisterHandlerTests
{
    [Fact]
    public async Task ResetsNumericValuesAndValidatesScalerStructure()
    {
        var d = new EmclBuilder().Build("d", BlueBook16Profile.Id);
        var r = d.Add(3, 0, LogicalName.Parse("1.0.1.8.0.255"));
        r.State.Set(2, Number(DlmsType.UInt32, 42));
        Assert.Equal(CosemError.Success, (await ExecuteOperationAsync(d, r, CosemOperationKind.Invoke, 1, Number(DlmsType.Int8, 0))).Error);
        Assert.Equal(0L, ReadStoredValue(r, 2).AsInt64());
        Assert.Equal(CosemError.TypeUnmatched, (await ExecuteOperationAsync(d, r, CosemOperationKind.Write, 3, DlmsValue.Structure())).Error);
    }
}
