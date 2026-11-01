using EMCL.Composition;
using EMCL.Core.Codecs;
using EMCL.Core.Objects;
using EMCL.Core.Values;
using EMCL.Objects.Association;
using EMCL.Protocol.Axdr;

namespace EMCL.Tests.Objects.Association;

public sealed class AssociationImporterTests
{
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
}
