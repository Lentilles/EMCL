using System.Collections.Immutable;
using EMCL.Composition;
using EMCL.Core.Access;
using EMCL.Core.Descriptors;
using EMCL.Core.Objects;
using EMCL.Core.Operations;
using EMCL.Core.Values;
using EMCL.Objects.Association;
using EMCL.Objects.Definitions;
using EMCL.Protocol.Apdu;
using EMCL.Core.Codecs;

namespace EMCL.Tests.Objects.Spodes;

public sealed class SpodesProfileTests
{
    [Theory]
    [InlineData(CosemProfile.Spodes4, "1:0 3:0 4:0 5:0 6:0 7:1 8:0 9:0 10:0 11:0 15:1 17:0 18:0 19:1 20:0 21:0 22:0 23:1 40:2 41:0 44:0 64:1 68:0 70:0 71:0 124:0")]
    [InlineData(CosemProfile.Spodes5, "1:0 3:0 4:0 5:0 6:0 7:1 8:0 9:0 10:0 11:0 15:1 17:0 18:0 19:1 20:0 21:0 22:0 23:1 29:2 40:3 41:0 42:0 43:0 44:0 45:0 47:2 48:0 64:1 68:0 70:2 71:0 80:0 81:0 82:0 83:0 84:0 85:0 86:0 90:1 91:4 92:4 101:0 102:0 103:0 104:0 105:0 124:0 128:0 129:0 151:1 5240:1 5241:1 5242:0 5243:0 5244:0 5245:0 5246:0")]
    public void ResolvesEveryExactProfileClassForLocalCreationAndImport(CosemProfile profile, string expectedKeys)
    {
        var keys = expectedKeys.Split(' ').Select(key => key.Split(':'))
            .Select(fields => new ClassKey(ushort.Parse(fields[0], System.Globalization.CultureInfo.InvariantCulture),
                byte.Parse(fields[1], System.Globalization.CultureInfo.InvariantCulture))).ToArray();
        Assert.Equal(keys, StandardDefinitions.Create(profile).Select(descriptor => descriptor.Key).OrderBy(key => key.ClassId));
        var builder = new EmclBuilder(profile);
        var device = builder.Build("device");
        var rows = keys.Select((key, index) =>
        {
            var obj = device.Add(key.ClassId, key.ClassVersion, LogicalName.Parse($"0.0.128.1.{index}.255"));
            Assert.True(obj.Descriptor.IsKnown);
            return AssociationHandler.Entry(obj, new ObjectAccessRights([], []));
        }).ToArray();
        var imported = builder.CreateImporter().Import(DlmsValue.Array(rows), "import", "public", 1);
        Assert.All(imported.Catalog.Objects, obj => Assert.True(obj.Descriptor.IsKnown));
    }

    [Fact]
    public async Task UsesProfileDateTagsAndValidatesNestedRelayParameters()
    {
        var four = new EmclBuilder(CosemProfile.Spodes4);
        var five = new EmclBuilder(CosemProfile.Spodes5);
        Assert.Equal(DlmsType.DateTime, four.Definitions.Find(new(8, 0))!.Attributes.Single(attribute => attribute.Index == 2).Type);
        var clock = five.Definitions.Find(new(8, 0))!;
        Assert.Equal(DlmsType.OctetString, clock.Attributes.Single(attribute => attribute.Index == 2).Type);
        Assert.Equal(DlmsType.DateTime, clock.Attributes.Single(attribute => attribute.Index == 5).Type);
        Assert.Null(five.Definitions.Find(new(70, 0)));
        Assert.Null(four.Definitions.Find(new(40, 3)));
        var device = five.Build("device");
        var relay = device.Add(70, 2, LogicalName.Parse("0.0.96.3.10.255"));
        var request = new LnRequest(0x41, new(CosemOperationKind.Invoke, 70, relay.LogicalName,
            DisconnectControlMembers.InitializeDelayed, DlmsValue.Structure(
                DlmsValue.Number(DlmsType.UInt8, 23), DlmsValue.Number(DlmsType.UInt8, 48), DlmsValue.Number(DlmsType.UInt16, 168))));
        var codec = new ApduCodec();
        byte[] encoded = codec.EncodeBytes(request);
        Assert.Equal(0x81, encoded[11]);
        var decoded = Assert.IsType<LnRequest>(codec.Decode(new(encoded)).Value);
        var result = await device.Pipeline.ExecuteAsync(decoded.Operation, device.LocalContext, TestContext.Current.CancellationToken);
        Assert.Equal(CosemResultCode.TypeUnmatched, result.Error);
    }

    [Fact]
    public void PreservesUnknownVersionsAndUserDefinitions()
    {
        var builder = new EmclBuilder(CosemProfile.Spodes5);
        var unknown = new CosemObject(new(new(40, 2), "Remote", [], [], false), LogicalName.Parse("0.0.25.9.0.255"));
        var rights = new ObjectAccessRights([new(2, 1, ImmutableArray<byte>.Empty)], [new(1, 1)]);
        var entry = AssociationHandler.Entry(unknown, rights);
        var descriptor = builder.CreateImporter().ParseEntry(entry, 1).Descriptor;
        Assert.False(descriptor.IsKnown);
        Assert.Equal(new ClassKey(40, 2), descriptor.Key);
        var custom = descriptor with
        {
            Name = "Custom",
            IsKnown = true
        };
        builder.Definitions.Register(custom);
        Assert.Same(custom, builder.CreateImporter().ParseEntry(entry, 1).Descriptor);
    }

    [Theory]
    [InlineData(DisconnectControlMembers.InitializeDelayed, 0x81)]
    [InlineData(DisconnectControlMembers.ConfirmDelayed, 0x82)]
    [InlineData(DisconnectControlMembers.BlockControl, 0x83)]
    public void PreservesUnsignedRelayMethodNumbersOnTheWire(sbyte method, byte wire)
    {
        var codec = new ApduCodec();
        var operation = new CosemOperation(CosemOperationKind.Invoke, CosemClassIds.DisconnectControl,
            LogicalName.Parse("0.0.96.3.10.255"), method);
        byte[] encoded = codec.EncodeBytes(new LnRequest(0x41, operation));
        Assert.Equal(wire, encoded[11]);
        Assert.Equal(operation, Assert.IsType<LnRequest>(codec.Decode(new(encoded)).Value).Operation);
    }
}
