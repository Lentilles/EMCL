using EMCL.Composition;
using EMCL.Core.Access;
using EMCL.Core.Objects;
using EMCL.Core.Values;
using EMCL.Examples.AssociationDump.Import;
using EMCL.Objects.Association;
using EMCL.Objects.Definitions;

namespace EMCL.Tests.Examples.AssociationDump;

public sealed class IdenticalObjectListDuplicateFilterTests
{
    [Fact]
    public void RemovesOnlyByteIdenticalEntriesBeforeStrictImport()
    {
        var builder = new EmclBuilder();
        var device = builder.Build("meter", BlueBook16Profile.Id);
        var obj = device.Add(CosemClassIds.Register, RegisterMembers.Version0,
            LogicalName.Parse("1.0.91.7.131.255"));
        var first = AssociationHandler.Entry(obj, new ObjectAccessRights(
            [new(RegisterMembers.Attributes.Value, (byte)AccessModeV3.Read, [])], []));
        var different = AssociationHandler.Entry(obj, new ObjectAccessRights(
            [new(RegisterMembers.Attributes.Value, (byte)AccessModeV3.Write, [])], []));

        var filtered = IdenticalObjectListDuplicateFilter.Apply(DlmsValue.Array(first, first), new());
        Assert.Single(filtered.Value.Items);
        Assert.Single(filtered.Warnings);
        Assert.Contains(obj.LogicalName.ToString(), filtered.Warnings[0], StringComparison.Ordinal);
        Assert.Single(builder.CreateImporter().Import(filtered.Value, "meter", BlueBook16Profile.Id,
            "public", AssociationLnMembers.Version3).Catalog.Objects);

        var conflicting = IdenticalObjectListDuplicateFilter.Apply(DlmsValue.Array(first, different), new());
        Assert.Equal(2, conflicting.Value.Items.Length);
        Assert.Empty(conflicting.Warnings);
        Assert.Throws<AssociationImportException>(() => builder.CreateImporter().Import(conflicting.Value,
            "meter", BlueBook16Profile.Id, "public", AssociationLnMembers.Version3));
    }
}
