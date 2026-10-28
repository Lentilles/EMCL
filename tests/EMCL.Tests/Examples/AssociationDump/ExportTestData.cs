using EMCL.Client.Association;
using EMCL.Composition;
using EMCL.Core.Access;
using EMCL.Core.Objects;
using EMCL.Examples.AssociationDump.Configuration;
using EMCL.Examples.AssociationDump.Export;
using EMCL.Objects.Association;
using EMCL.Objects.Definitions;
using EMCL.Protocol.Apdu;

namespace EMCL.Tests.Examples.AssociationDump;

internal static class ExportTestData
{
    internal static MeterConfiguration Configuration(string path) => new()
    {
        Connection = new()
        {
            Host = "127.0.0.1",
            TcpPort = 4059,
            ClientWrapperPort = 16,
            ServerWrapperPort = 1
        },
        Association = new()
        {
            ProfileId = BlueBook16Profile.Id,
            Version = AssociationLnMembers.Version3,
            LogicalName = "0.0.40.0.0.255",
            LogicalDeviceId = "meter-1",
            AssociationId = "public"
        },
        Output = new()
        {
            Path = path,
            Overwrite = true
        }
    };

    internal static AssociationDocument Document(MeterConfiguration configuration, byte attributeMode, byte methodMode)
    {
        var builder = new EmclBuilder();
        var device = builder.Build(configuration.Association.LogicalDeviceId, configuration.Association.ProfileId);
        var known = device.Add(CosemClassIds.Register, RegisterMembers.Version0, LogicalName.Parse("1.0.1.8.0.255"));
        var unknown = device.Add(9000, 9, LogicalName.Parse("0.0.96.1.0.255"));
        var rights = new ObjectAccessRights(
            [new(RegisterMembers.Attributes.Value, attributeMode, [1, 2]), new(7, 0, [])],
            [new(RegisterMembers.Methods.Reset, methodMode), new(-1, 0)]);
        var unknownRights = new ObjectAccessRights([new(-2, attributeMode, [])], [new(-3, methodMode)]);
        var list = EMCL.Core.Values.DlmsValue.Array(AssociationHandler.Entry(known, rights), AssociationHandler.Entry(unknown, unknownRights));
        var imported = builder.CreateImporter().Import(list, device.Catalog.LogicalDeviceId, device.Catalog.ProfileId,
            configuration.Association.AssociationId, configuration.Association.Version);
        return AssociationDocumentFactory.Create(imported, ConfigurationFile.Validate(configuration, System.IO.Path.GetTempPath()),
            new(DlmsNegotiation.Version6, DlmsConformance.Get | DlmsConformance.BlockTransferWithGet, 512, 1024),
            DateTimeOffset.UnixEpoch);
    }
}
