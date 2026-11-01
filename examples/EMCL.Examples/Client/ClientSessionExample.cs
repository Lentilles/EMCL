using System.Collections.Immutable;
using EMCL.Client.Sessions;
using EMCL.Composition;
using EMCL.Core.Access;
using EMCL.Core.Objects;
using EMCL.Core.Values;
using EMCL.Objects.Definitions;

namespace EMCL.Examples.Client;

internal static class ClientSessionExample
{
    internal static async Task RunAsync()
    {
        var builder = new EmclBuilder();
        var meter = builder.Build("local-meter");
        var register = meter.Add(CosemClassIds.Register, RegisterMembers.Version0, LogicalName.Parse("1.0.1.8.0.255"));
        register.State.Set(RegisterMembers.Attributes.Value, DlmsValue.Number(DlmsType.UInt32, 42));
        var association = meter.Add(CosemClassIds.AssociationLn, AssociationLnMembers.Version3,
            LogicalName.Parse("0.0.40.0.0.255"));
        var rights = new ObjectAccessRights(
            [new(RegisterMembers.Attributes.Value, (byte)(AccessModeV3.Read | AccessModeV3.Write), [])],
            [new(RegisterMembers.Methods.Reset, (byte)AccessModeV3.Invoke)]);
        meter.Associations.Bind(association.LogicalName, new("public", AssociationLnMembers.Version3,
            ImmutableDictionary<LogicalName, ObjectAccessRights>.Empty.Add(register.LogicalName, rights)));

        await using var client = new DlmsClientSession(new LocalMeterChannel(meter));
        var negotiated = await client.AssociateAsync();
        Console.WriteLine($"Client associated: {negotiated.Conformance}");
        var value = await client.GetAsync(CosemClassIds.Register, register.LogicalName, RegisterMembers.Attributes.Value);
        Console.WriteLine($"Client GET: {value.AsUInt64()}");
        await client.SetAsync(CosemClassIds.Register, register.LogicalName, RegisterMembers.Attributes.Value,
            DlmsValue.Number(DlmsType.UInt32, 100));
        await client.ActionAsync(CosemClassIds.Register, register.LogicalName, RegisterMembers.Methods.Reset,
            DlmsValue.Number(DlmsType.Int8, 0));
        var imported = await client.LoadCatalogAsync(builder.CreateImporter(), "remote-meter",
            "public", AssociationLnMembers.Version3);
        Console.WriteLine($"Client imported catalog: {imported.Catalog.Objects.Length} objects");
        await client.ReleaseAsync();
        Console.WriteLine($"Client state: {client.State}");
    }
}
