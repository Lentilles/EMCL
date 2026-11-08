using EMCL.Composition;
using EMCL.Core.Objects;
using EMCL.Core.Operations;
using EMCL.Core.Values;

namespace EMCL.Tests.Objects.Association;

public sealed class AssociationHandlerTests
{
    [Fact]
    public async Task ChangesVisibilityAndUsersWithoutDeletingSharedObjects()
    {
        var d = new EmclBuilder().Build("d");
        var association = d.Add(15, 3, LogicalName.Parse("0.0.40.0.0.255"));
        var entry = Entry(1, 0, "0.0.96.1.0.255");
        Assert.Equal(CosemResultCode.Success, (await ExecuteOperationAsync(d, association, CosemOperationKind.Invoke, 3, entry)).Error);
        Assert.Single(d.Associations.Get(association.LogicalName)!.Rights);
        Assert.Equal(CosemResultCode.Success, (await ExecuteOperationAsync(d, association, CosemOperationKind.Invoke, 4, entry)).Error);
        Assert.Empty(d.Associations.Get(association.LogicalName)!.Rights);
        Assert.True(d.Catalog.Find(LogicalName.Parse("0.0.96.1.0.255")) != null);
        Assert.Equal(CosemResultCode.ReadWriteDenied, (await ExecuteOperationAsync(d, association, CosemOperationKind.Invoke, 1, DlmsValue.Octets([1]))).Error);
        var user = DlmsValue.Structure(Number(DlmsType.UInt8, 1), new DlmsValue(DlmsType.VisibleString, "operator"));
        Assert.Equal(CosemResultCode.Success, (await ExecuteOperationAsync(d, association, CosemOperationKind.Invoke, 5, user)).Error);
        Assert.Single(ReadStoredValue(association, 10).Items);
        Assert.Equal(CosemResultCode.Success, (await ExecuteOperationAsync(d, association, CosemOperationKind.Invoke, 6, user)).Error);
        Assert.Empty(ReadStoredValue(association, 10).Items);
    }
}
