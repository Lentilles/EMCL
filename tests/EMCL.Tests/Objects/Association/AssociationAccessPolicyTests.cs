using EMCL.Core.Access;
using EMCL.Core.Objects;
using EMCL.Core.Operations;
using EMCL.Core.Values;
using EMCL.Objects.Processing;
using EMCL.Profiles.Spodes;

namespace EMCL.Tests.Objects.Association;

public sealed class AssociationAccessPolicyTests
{
    [Fact]
    public void InterpretsVersionSpecificAccessModes()
    {
        var obj = new CosemObject(new(new(Spodes4Profile.Id, 1, 0), "Data", [new(2, "value")], [new(1, "method")]), LogicalName.Parse("0.0.96.1.0.255"));
        var op = new CosemOperation(CosemOperationKind.Read, 1, obj.LogicalName, 2);
        var p = new AssociationAccessPolicy();
        OperationContext C(byte v, byte mode) => new("d", new("a", v, ImmutableDictionary<LogicalName, ObjectAccessRights>.Empty.Add(obj.LogicalName, new([new(2, mode, [1])], [new(1, mode)]))));
        Assert.True(p.Allows(obj, op, C(1, 1)));
        Assert.True(!p.Allows(obj, op with
        {
            Kind = CosemOperationKind.Write
        }, C(1, 1)));
        Assert.True(!p.Allows(obj, op, C(1, 4)));
        Assert.True(p.Allows(obj, op, C(1, 4) with
        {
            AuthenticatedRequest = true
        }));
        Assert.True(!p.Allows(obj, op, C(3, 13) with
        {
            AuthenticatedRequest = true
        }));
        Assert.True(p.Allows(obj, op, C(3, 13) with
        {
            AuthenticatedRequest = true,
            EncryptedRequest = true
        }));
        Assert.True(!p.Allows(obj, op, C(3, 0xe1)));
        Assert.True(p.Allows(obj, op, C(3, 0xe1) with
        {
            AuthenticatedResponse = true,
            EncryptedResponse = true,
            SignedResponse = true
        }));
        Assert.True(!p.Allows(obj, op with
        {
            Selection = new(2, DlmsValue.Null)
        }, C(1, 3)));
        Assert.True(p.Allows(obj, op with
        {
            Kind = CosemOperationKind.Invoke,
            Index = 1
        }, C(1, 2) with
        {
            AuthenticatedRequest = true
        }));
    }
}
