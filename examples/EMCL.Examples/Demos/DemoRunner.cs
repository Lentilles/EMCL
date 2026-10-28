using System.Buffers;
using System.Collections.Immutable;
using EMCL.Composition;
using EMCL.Core.Access;
using EMCL.Core.Codecs;
using EMCL.Core.Objects;
using EMCL.Core.Operations;
using EMCL.Core.Values;
using EMCL.Examples.Client;
using EMCL.Examples.Handlers;
using EMCL.Examples.Registers;
using EMCL.Examples.Transport;
using EMCL.Framing.Wrapper;
using EMCL.Framing;
using EMCL.Messaging;
using EMCL.Objects.Association;
using EMCL.Objects.Definitions;
using EMCL.Profiles.BlueBook;
using EMCL.Protocol.Apdu;
using EMCL.Protocol.Axdr;
using EMCL.Transport.Channels;
using EMCL.Transport.Streams;

namespace EMCL.Examples.Demos;

internal static class DemoRunner
{
    private const byte ExampleInvokeId = 0xc1;

    internal static async Task RunAsync()
    {
        await ClientSessionExample.RunAsync();
        var builder = new EmclBuilder();
        var device = builder.Build("meter-1", BlueBook16Profile.Id);
        await RegisterExamples.RunAsync(device);
        var register = device.Add(CosemClassIds.Register, RegisterMembers.Version0, LogicalName.Parse("1.0.1.8.0.255"));
        register.State.Set(RegisterMembers.Attributes.Value, DlmsValue.Number(DlmsType.UInt32, 12345));
        register.State.Set(RegisterMembers.Attributes.ScalerUnit, DlmsValue.Structure(DlmsValue.Number(DlmsType.Int8, -3), DlmsValue.Number(DlmsType.Enum, 30)));
        var operation = new CosemOperation(CosemOperationKind.Read, CosemClassIds.Register, register.LogicalName, RegisterMembers.Attributes.Value);
        var response = await DlmsEndpoint.ExecuteAsync(new LnRequest(ExampleInvokeId, operation), device.Pipeline, device.LocalContext);
        Console.WriteLine($"GET {register.LogicalName}: {response.Result.Value!.AsUInt64()}");
        Console.WriteLine($"APDU: {Convert.ToHexString(new ApduCodec().EncodeBytes(response))}");
        // Replace one object's handler without changing codecs, transport, or other objects.
        device.Handlers.ForInstance("meter-1", register.LogicalName, new ExampleHandler());
        var overridden = await device.Pipeline.ExecuteAsync(operation, device.LocalContext);
        Console.WriteLine($"Custom handler: {overridden.Value!.AsUInt64()}");
        // A binary object_list contains identities and access rights, but no current values.
        var rights = new ObjectAccessRights([new(RegisterMembers.Attributes.Value, (byte)AccessModeV3.Read, [])],
            [new(RegisterMembers.Methods.Reset, (byte)AccessModeV3.Invoke)]);
        var objectList = DlmsValue.Array(AssociationHandler.Entry(register, rights));
        var binaryList = new AxdrCodec().EncodeBytes(objectList);
        var imported = builder.CreateImporter().Import(new ReadOnlySequence<byte>(binaryList), "imported-meter", BlueBook16Profile.Id, "public", AssociationLnMembers.Version3);
        var importedDevice = builder.Build("imported-meter", BlueBook16Profile.Id, imported);
        Console.WriteLine($"Imported objects: {importedDevice.Catalog.Objects.Length}");
        Console.WriteLine($"Imported value: {(await importedDevice.Pipeline.ExecuteAsync(operation, importedDevice.CreateContext())).Error}");
        // Read exactly the same APDU from Wrapper framing over an arbitrary Stream.
        var request = new LnRequest(ExampleInvokeId, operation);
        var payload = new ApduCodec().EncodeBytes(request);
        var wrapperBytes = new WrapperCodec().EncodeBytes(new(16, 1, payload.ToImmutableArray()));
        await using (var channel = new FramedApduChannel<WrapperFrame>(new StreamByteTransport(new MemoryStream(wrapperBytes)), new WrapperCodec(), new WrapperApduAdapter(1, 16)))
        {
            await using (var endpoint = new DlmsEndpoint(channel))
            {
                var received = (LnRequest)(await endpoint.ReceiveAsync())!;
                Console.WriteLine($"Wrapper request: {received.Operation.LogicalName}, attribute {received.Operation.Index}");
            }
        }

        // A message-oriented custom transport bypasses byte framing entirely.
        await using (var endpoint = new DlmsEndpoint(new ExampleApduChannel(payload)))
        {
            Console.WriteLine($"Custom channel APDU: {(await endpoint.ReceiveAsync())!.GetType().Name}");
        }
    }
}
