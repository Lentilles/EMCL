using EMCL.Composition;
using EMCL.Core.Codecs;
using EMCL.Core.Descriptors;
using EMCL.Core.Objects;
using EMCL.Core.Values;
using EMCL.Objects.Association;
using EMCL.Protocol.Axdr;

namespace EMCL.Tests.Objects.Association;

public sealed class AssociationImporterTests
{
    [Theory]
    [InlineData(CosemProfile.Cosem, 1, 6, 2)]
    [InlineData(CosemProfile.Cosem, 3, 0x63, 0x09)]
    [InlineData(CosemProfile.Spodes4, 1, 6, 2)]
    [InlineData(CosemProfile.Spodes4, 3, 0x63, 0x09)]
    [InlineData(CosemProfile.Spodes5, 1, 6, 2)]
    [InlineData(CosemProfile.Spodes5, 3, 0x63, 0x09)]
    public void ImportsCommunicationClassesWithMeterAccessRights(
        CosemProfile profile, byte associationVersion, byte attributeMode, byte resetMode)
    {
        const string pppName = "0.0.25.3.0.255";
        const string protectionName = "0.0.44.2.0.255";
        var pppEntry = CommunicationEntry(44, 0, pppName, 3, attributeMode, null);
        var protectionEntry = CommunicationEntry(124, 0, protectionName, 8, 1, resetMode);
        var wire = new AxdrCodec().EncodeBytes(DlmsValue.Array(pppEntry, protectionEntry));
        var importer = new EmclBuilder(profile).CreateImporter();
        var imported = importer.Import(new ReadOnlySequence<byte>(wire), "device", "meter", associationVersion);

        var ppp = imported.Catalog.Find(LogicalName.Parse(pppName))!;
        var protection = imported.Catalog.Find(LogicalName.Parse(protectionName))!;
        Assert.Equal(new ClassKey(44, 0), ppp.Descriptor.Key);
        Assert.Equal(new ClassKey(124, 0), protection.Descriptor.Key);
        Assert.True(ppp.Descriptor.IsKnown);
        Assert.True(protection.Descriptor.IsKnown);
        Assert.Equal("PPP Setup", ppp.Descriptor.Name);
        Assert.Equal("Communication Port Protection", protection.Descriptor.Name);
        Assert.Equal(
            [(1, "logical_name", DlmsType.OctetString), (2, "PHY_reference", DlmsType.OctetString),
             (3, "LCP_options", DlmsType.Array), (4, "IPCP_options", DlmsType.Array), (5, "PPP_authentication", null)],
            ppp.Descriptor.Attributes.Select(attribute => ((int)attribute.Index, attribute.Name, attribute.Type)));
        Assert.Equal(
            [(1, "logical_name", DlmsType.OctetString), (2, "protection_mode", DlmsType.Enum),
             (3, "allowed_failed_attempts", DlmsType.UInt16), (4, "initial_lockout_time", DlmsType.UInt32),
             (5, "steepness_factor", DlmsType.UInt8), (6, "max_lockout_time", DlmsType.UInt32),
             (7, "port_reference", DlmsType.OctetString), (8, "protection_status", DlmsType.Enum),
             (9, "failed_attempts", DlmsType.UInt32), (10, "cumulative_failed_attempts", DlmsType.UInt32)],
            protection.Descriptor.Attributes.Select(attribute => ((int)attribute.Index, attribute.Name, attribute.Type)));
        Assert.Empty(ppp.Descriptor.Methods);
        var reset = Assert.Single(protection.Descriptor.Methods);
        Assert.Equal(1, reset.Index);
        Assert.Equal("reset", reset.Name);
        Assert.Equal(DlmsType.Int8, reset.ParameterType);
        Assert.Null(reset.ParameterConstraint!(Number(DlmsType.Int8, 0)));
        Assert.NotNull(reset.ParameterConstraint!(Number(DlmsType.Int8, 1)));

        var pppRights = imported.Access.Rights[ppp.LogicalName];
        var pppAccess = Assert.Single(pppRights.Attributes);
        Assert.Equal(3, pppAccess.Index);
        Assert.Equal(attributeMode, pppAccess.RawMode);
        Assert.Equal((byte)2, Assert.Single(pppAccess.Selectors));
        Assert.Empty(pppRights.Methods);
        var protectionRights = imported.Access.Rights[protection.LogicalName];
        var statusAccess = Assert.Single(protectionRights.Attributes);
        Assert.Equal(8, statusAccess.Index);
        Assert.Equal(1, statusAccess.RawMode);
        var resetAccess = Assert.Single(protectionRights.Methods);
        Assert.Equal(reset.Index, resetAccess.Index);
        Assert.Equal(resetMode, resetAccess.RawMode);

        var future = importer.Import(DlmsValue.Array(
            CommunicationEntry(44, 1, pppName, 3, attributeMode, null),
            CommunicationEntry(124, 1, protectionName, 8, 1, resetMode)), "device", "meter", associationVersion);
        Assert.All(future.Catalog.Objects, obj =>
        {
            Assert.False(obj.Descriptor.IsKnown);
            Assert.Equal(1, obj.Descriptor.Key.ClassVersion);
        });
    }

    [Fact]
    public async Task ImportsAtomicallyAndResolvesExactVersions()
    {
        var b = new EmclBuilder();
        var i = b.CreateImporter();
        var list = DlmsValue.Array(Entry(3, 0, "1.0.1.8.0.255"), Entry(9000, 9, "0.0.96.1.0.255", 1));
        var wire = new AxdrCodec().EncodeBytes(list);
        var imported = i.Import(new ReadOnlySequence<byte>(wire), "d", "public", 1);
        Assert.Equal(2, imported.Catalog.Objects.Length);
        Assert.True(imported.Catalog.Find(LogicalName.Parse("1.0.1.8.0.255"))!.Descriptor.IsKnown);
        var unknown = imported.Catalog.Find(LogicalName.Parse("0.0.96.1.0.255"))!;
        Assert.True(!unknown.Descriptor.IsKnown && unknown.Descriptor.Attributes[0].Type == null);
        using var stream = new MemoryStream(wire);
        Assert.Equal(2, (await i.ImportAsync(stream, "s", "a", 1, cancellationToken: TestContext.Current.CancellationToken)).Catalog.Objects.Length);
        b.Definitions.Register(unknown.Descriptor with
        {
            Name = "Custom",
            IsKnown = true
        });
        imported.Catalog.Rebind(b.Definitions);
        Assert.Equal("Custom", unknown.Descriptor.Name);
        foreach (var bad in new[]
        {
            DlmsValue.Array(list.Items[0], list.Items[0]),
            DlmsValue.Array(Entry(3, 0, "1.0.1.8.0.255"), Entry(3, 1, "1.0.2.8.0.255")),
            DlmsValue.Array(Number(DlmsType.UInt8, 0))
        }

        )
        {
            Assert.Throws<AssociationImportException>(() => i.Import(bad, "d", "a", 1));
        }

        Assert.Throws<AssociationImportException>(() => i.Import(new ReadOnlySequence<byte>(wire.Concat(new byte[] { 0 }).ToArray()), "d", "a", 1));
        Assert.Throws<AssociationImportException>(() => i.Import(new ReadOnlySequence<byte>(wire[..^1]), "d", "a", 1));
        Assert.Equal(2, imported.Catalog.Objects.Length);
        var future = i.Import(DlmsValue.Array(Entry(3, 99, "1.0.1.8.0.255")), "d", "a", 3);
        Assert.True(!future.Catalog.Objects[0].Descriptor.IsKnown);
        Assert.Equal((byte)99, future.Catalog.Objects[0].Descriptor.Key.ClassVersion);
    }

    private static DlmsValue CommunicationEntry(ushort id, byte version, string name, sbyte attributeIndex,
        byte attributeMode, byte? resetMode) => DlmsValue.Structure(
        Number(DlmsType.UInt16, id),
        Number(DlmsType.UInt8, version),
        DlmsValue.Octets(LogicalName.Parse(name).ToBytes()),
        DlmsValue.Structure(
            DlmsValue.Array(DlmsValue.Structure(Number(DlmsType.Int8, attributeIndex),
                Number(DlmsType.Enum, attributeMode), DlmsValue.Array(Number(DlmsType.Int8, 2)))),
            resetMode is { } mode
                ? DlmsValue.Array(DlmsValue.Structure(Number(DlmsType.Int8, 1), Number(DlmsType.Enum, mode)))
                : DlmsValue.Array()));
}
