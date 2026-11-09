# EMCL

An extensible DLMS/COSEM library for .NET 10. A universal object model, binary codecs, local operation execution, a Logical Name client session, and independent framing and transport adapters.

The library is distributed under the [MIT license](LICENSE).

## Features

- Logical Name client associations: GET/SET/ACTION, selective access, GET and SET block transfer, notifications, and None, LLS, and HLS High (AES-128) authentication.
- Association LN v1/v3 reading: full catalog import or streaming `object_list` entries with progress reporting.
- Wrapper and HDLC over TCP, Serial, or a custom transport; console export of the catalog and access rights to JSON.
- Local models for Data, Register, Extended Register, Demand Register, Register Activation, Clock, Profile Generic, and Association LN; custom classes and handlers.
- Independent A-XDR, ACSE, and LN APDU codecs with data size and nesting limits.

Short Name, protected APDUs, HLS SHA-256/GMAC, ACTION block transfer, GBT, and a ready-to-use server session are not implemented yet. The HDLC channel performs SNRM/UA setup and I/RR exchange; each outgoing APDU must fit a single negotiated information frame. See the [capability matrix](docs/COVERAGE.md) for details.

## Contents

- [Build and verification](#build-and-verification) · [Local NuGet packages](#local-nuget-packages)
- [Publishing to NuGet](docs/PUBLISHING.md)
- [Exporting a real meter association](#exporting-a-real-meter-association)
- [Repository structure](#repository-structure) · [Assemblies](#assemblies)
- [Quick example](#quick-example) · [Association import](#association-import)
- [Custom objects and handlers](#custom-objects-and-handlers)
- [Transport and framing](#transport-and-framing) · [Logical Name client session](#logical-name-client-session)
- [Blocks, limits, and local execution](#blocks-limits-and-local-execution) · [License](#license)

## Build and verification

Building requires a stable .NET SDK 10.0.100 or later in the 10.0 line; SDK selection and the test runner are configured in [global.json](global.json). `EMCL.Transport` uses Microsoft's `System.IO.Ports` package for Serial; the other library projects have no external NuGet dependencies. Tests use xUnit v3 and Microsoft Testing Platform.

Run the following commands from the repository root. The local `--demo` demonstration does not require a meter connection:

```sh
dotnet restore EMCL.slnx --disable-parallel
dotnet build EMCL.slnx -c Release --no-restore -m:1
dotnet test --project tests/EMCL.Tests/EMCL.Tests.csproj -c Release --no-build --no-restore
dotnet run --project examples/EMCL.Examples -c Release --no-build -- --demo
```

`EMCL.Tests` contains `[Fact]` tests and parameterized `[Theory]` tests organized by subsystem. Reference messages are checked as individual test cases. xUnit analyzers are enabled; asynchronous tests use the test context's cancellation token.

The public API has XML documentation in English. XML files are generated alongside the DLLs and included in NuGet packages. Missing documentation (`CS1591`), compiler warnings, and warnings from enabled analyzers fail the build. Development rules and verification commands are described in [CONTRIBUTING.md](CONTRIBUTING.md).

## Local NuGet packages

After restoring dependencies, build the packages without publishing them to NuGet.org:

```sh
dotnet build EMCL.slnx -c Release --no-restore -m:1
dotnet pack EMCL.slnx -c Release --no-restore --no-build --disable-build-servers -m:1 \
  -p:PackageVersion=0.1.0-preview.1 -p:PackageOutputPath="$PWD/packages"
```

The `packages/` directory will contain `EMCL.0.1.0-preview.1.nupkg` and packages for the seven library assemblies it depends on, along with matching `.snupkg` symbol packages. Each package includes the [NuGet README](nuget/README.md), the complete license text, and `MIT` license metadata. The shared version and metadata are configured in `Directory.Build.props`. Examples and tests are not packaged; `packages/` is excluded from Git.

To use the packages in another project, add the absolute path to `packages/` as a local NuGet source and install the same version of `EMCL`:

```sh
dotnet nuget add source /absolute/path/to/EMCL/packages --name emcl-local
dotnet add package EMCL --version 0.1.0-preview.1
```

Run the installation command in the consuming project's directory, targeting `net10.0` or a compatible framework. The version `0.1.0-preview.1` in these commands is an example local version. `dotnet pack` only creates files; publishing is performed separately with `dotnet nuget push`.

To prepare a release with dependency restore, build, and tests, run
`bash scripts/pack.sh 0.1.0-preview.1`. The output is saved in the separate
`packages/0.1.0-preview.1/` directory. Instructions for publishing all eight
packages and their symbols are in [docs/PUBLISHING.md](docs/PUBLISHING.md).

GitHub Actions builds, tests, and packages the solution on branch pushes and pull
requests. Pushing a version tag such as `v0.1.0-preview.1` publishes all packages
and symbols to NuGet.org through Trusted Publishing (OIDC). Configure the
`NUGET_USER` Actions variable, the `nuget` environment, and the NuGet.org trust
policy as described in the [setup instructions](docs/PUBLISHING.md#one-time-setup).

## Exporting a real meter association

The console example connects over Wrapper/TCP, an HDLC/TCP gateway, or directly over Serial, reads `object_list` (including GET block transfer), and saves JSON with OBIS codes, classes, attributes, methods, and raw and decoded access rights.

```sh
cp examples/EMCL.Examples/meter.example.json examples/EMCL.Examples/meter.local.json
dotnet run --project examples/EMCL.Examples -c Release
```

For Serial, copy `examples/EMCL.Examples/meter.serial.example.json` to `meter.local.json` in the same directory instead of using the TCP template, then set the port name and line parameters. Serial supports HDLC and Wrapper; IEC 62056-21 sign-on and automatic baud-rate switching are not performed.

To run from an IDE, select `EMCL.Examples` as the startup project and click **Run**. With no arguments, the application prefers `meter.local.json`, then `meter.example.json`. Before running, edit the transport, addresses, TCP port, association version, and authentication settings in the configuration. The console prints the full output path. The output path and overwrite permission are also configured in the file. Relative paths are resolved against the directory containing the configuration copy next to the application. Password fields are excluded from the export.

[Configuration parameters and JSON schema](examples/EMCL.Examples/README.md) · [TCP template](examples/EMCL.Examples/meter.example.json) · [Serial template](examples/EMCL.Examples/meter.serial.example.json).

## Repository structure

```text
src/                         Library projects
  EMCL.Core/                 Access, Codecs, Descriptors, Objects, Operations,
                             Pipeline, State, Validation, Values
  EMCL.Protocol/             Acse, Apdu, Axdr, BlockTransfer, Internal
  EMCL.Client/               Association, Exceptions, Internal, Sessions
  EMCL.Objects/              Association, Catalog, Clock, Definitions, Handlers,
                             Internal, Processing, ProfileGeneric, Registers
  EMCL.Framing.Hdlc/         Frames, Llc, Segmentation
  EMCL.Framing.Wrapper/
  EMCL.Transport/            Channels, Streams
  EMCL/                      Composition, Framing, Messaging
tests/EMCL.Tests/            Tests by subsystem; shared data and doubles in Support
examples/EMCL.Examples/      Runnable examples
docs/                       Capability matrix and sources
```

Directories group files by responsibility. Each namespace matches the project's root namespace and relative path: for example, `src/EMCL.Core/Access/AttributeAccess.cs` uses `EMCL.Core.Access`, and `src/EMCL.Protocol/Acse/AcseCodec.cs` uses `EMCL.Protocol.Acse`. The `IDE0130` analyzer checks this during builds.

## Assemblies

| Assembly | Purpose |
| --- | --- |
| `EMCL.Core` | Identifiers, values, descriptors, state, and extension contracts |
| `EMCL.Protocol` | A-XDR, BER/ACSE, LN APDUs, and GET block assembly |
| `EMCL.Client` | LN client associations, GET/SET/ACTION, invoke IDs, deadlines, and catalog loading |
| `EMCL.Objects` | Catalog, association import, dispatcher, and local handlers |
| `EMCL.Framing.Hdlc` | Type 3 frames, CRC, LLC, and segment assembly |
| `EMCL.Framing.Wrapper` | Wrapper v1 |
| `EMCL.Transport` | `IByteTransport`, `IApduChannel`, `TcpByteTransport`, `SerialByteTransport`, `StreamByteTransport`, and a channel with an injectable frame codec |
| `EMCL` | Composition of class definitions, adapters, Wrapper/HDLC APDU channels, and endpoints |

`Protocol` has no references to transport, HDLC, or Wrapper. Standard definitions and handlers live in `Objects`; each composed device gets its own handler instances.

```text
IByteTransport
  → IIncrementalCodec<Frame>
  → IFramePayloadAdapter<Frame>       HDLC: segments → LLC; Wrapper: payload
  → IApduChannel
  → IIncrementalCodec<DlmsApdu>
  → IDlmsPipeline                    middleware → access → validation → handler
```

The reverse direction uses the same codecs. `GetBlockAssembler` assembles xDLMS data independently of HDLC segmentation.

## Quick example

```csharp
using EMCL.Composition;
using EMCL.Core.Objects;
using EMCL.Core.Operations;
using EMCL.Core.Values;
using EMCL.Objects.Definitions;

var device = new EmclBuilder().Build("meter-1");
var register = device.Add(CosemClassIds.Register, RegisterMembers.Version0, LogicalName.Parse("1.0.1.8.0.255"));
register.State.Set(RegisterMembers.Attributes.Value, DlmsValue.Number(DlmsType.UInt32, 12345));

var result = await device.Pipeline.ExecuteAsync(
    new(CosemOperationKind.Read, CosemClassIds.Register, register.LogicalName, RegisterMembers.Attributes.Value),
    device.LocalContext);
Console.WriteLine(result.Value!.AsUInt64());
```

The model is generic: class ID, version, logical name, descriptor, and values by index. `ClassKey` contains the class ID and exact version; definitions can be replaced through `ClassRegistry`. By default, time in Clock, Extended Register, and Demand Register is encoded as `DlmsType.OctetString`; for a meter using the `DlmsType.DateTime` tag, register the appropriate definitions through `ClockClassDefinition`, `ExtendedRegisterClassDefinition`, and `DemandRegisterClassDefinition` before import or `Build`. Use separate `EmclBuilder` instances for different definition sets. Each logical device uses one version of each class; devices are independent.

`DlmsValue` preserves the DLMS tag. Enum and unsigned, and array and structure remain distinct. Bytes and collections are copied into immutable representations. `CosemDateTime` preserves all 12 octets, including special fields, deviation, and status; `ToDateTimeOffset()` is valid only for a concrete date with an offset.

Application code can use named identifiers: `CosemClassIds.Register`, `RegisterMembers.Version0`, `RegisterMembers.Attributes.Value`, and `RegisterMembers.Methods.Reset`. APDU tags use `ApduTag`; access modes use `AttributeAccessModeV1`, `MethodAccessModeV1`, and `AccessModeV3`. Custom classes do not require casting identifiers to enums: the model still accepts arbitrary class IDs and indices.

## Association import

The file contains exactly one A-XDR `object_list` value, without a GET-response, Wrapper, LLC, or HDLC envelope. The stream is read to EOF; trailing bytes and incomplete data are rejected. For a network stream, first obtain the complete value from an APDU or GET blocks.

```csharp
using EMCL.Composition;
using EMCL.Core.Objects;
using EMCL.Core.Operations;
using EMCL.Objects.Definitions;

var builder = new EmclBuilder();
await using var file = File.OpenRead("object-list.bin");
var imported = await builder.CreateImporter().ImportAsync(
    file, "meter-1", "public-client", associationVersion: AssociationLnMembers.Version1);
var device = builder.Build("meter-1", imported);

var response = await device.Pipeline.ExecuteAsync(
    new(CosemOperationKind.Read, CosemClassIds.Register, LogicalName.Parse("1.0.1.8.0.255"), RegisterMembers.Attributes.Value),
    device.CreateContext());
```

Import atomically creates a new catalog; `object_list` contains no attribute values. Reading an unloaded value returns `ObjectUnavailable`. Duplicate logical names, invalid structures, and different versions of the same class within a device are rejected.

Specify the Association LN version explicitly: v1 rights are enumerations, while v3 rights are bit requirements. They are stored in `AssociationAccessContext`, separately from objects. If the import belongs to an association object other than `0.0.40.0.0.255`, pass `associationObject` to `Build`.

`CreateContext()` obtains the current permission snapshot. After local Association add/remove operations, create a new context; previously issued snapshots are immutable. Removing an object from one association removes its visibility while retaining the shared device object.

A context without an association (`LocalContext`) is intended for local use and does not restrict access by association permissions. A context with an association checks permissions; a missing permission means access is denied. A trusted application layer sets authentication, encryption, and signature flags after the corresponding checks. ACSE codecs do not authenticate peers themselves.

## Custom objects and handlers

Unknown class ID/version pairs are preserved without automatically selecting the nearest known version. `object_list` provides identifiers and permissions, but not attribute types or method implementations. Add a descriptor explicitly:

```csharp
using EMCL.Core.Descriptors;
using EMCL.Core.Values;

var key = new ClassKey(9000, 2);
builder.Definitions.Register(new CosemClassDescriptor(
    key, "Manufacturer object",
    [new(1, "logical_name", DlmsType.OctetString), new(2, "reading", DlmsType.UInt16)],
    [new(1, "command", DlmsType.UInt8)]));

// For previously imported instances:
device.Catalog.Rebind(builder.Definitions);
```

Implement `ICosemObjectHandler` or derive from `StoredValueHandler` and override the required operation. A complete compilable example is available in `EMCL.Examples`.

```csharp
device.Handlers.ForClass(key, myHandler);
device.Handlers.ForInstance("meter-1", LogicalName.Parse("0.0.128.1.0.255"), specialHandler);
```

Precedence: instance handler → custom class/version handler → standard handler → generic handler. Instance lookup includes the logical device identifier.

To replace a single attribute or method, wrap the existing handler:

```csharp
using EMCL.Core.Operations;
using EMCL.Core.Values;
using EMCL.Objects.Handlers;
using EMCL.Objects.Definitions;

var target = device.Catalog.Find(LogicalName.Parse("1.0.1.8.0.255"))!;
var original = device.Handlers.Resolve(device.Catalog.LogicalDeviceId, target);
var overridden = new ObjectHandlerOverrides(original)
    .OnRead(RegisterMembers.Attributes.Value, (obj, operation, context, ct) =>
        ValueTask.FromResult(OperationResult.Ok(DlmsValue.Number(DlmsType.UInt32, 42))));
device.Handlers.ForInstance(device.Catalog.LogicalDeviceId, target.LogicalName, overridden);
```

Other extension points:

- `IClassDefinitionProvider`: an external definition catalog; the most recently registered provider takes precedence, and explicit definitions override providers.
- `ICosemObjectFactory` and `IObjectStateStore`: object creation and state storage.
- `IValueConverter`: conversion of incoming Write/Invoke values before validation.
- `IValueValidator` and `IAccessPolicy`: operation and permission checks.
- `IDlmsMiddleware`: operation interception in registration order; `device.Pipeline` allows replacing the entire operation pipeline.
- `IClockSource`: the clock source; `ICaptureValueSource`: retrieval of captured values.
- `TimeProvider` and `IDemandValueCalculator`: measurement time and Demand Register calculations, configured through the builder's `MeasurementTimeProvider` and `DemandCalculatorFactory`.
- `IIncrementalCodec<T>`, `IFramePayloadAdapter<T>`, `IByteTransport`, and `IApduChannel`: independent replacement of any communication layer.

Configure registries before concurrent use. The default dispatcher serializes operations on each object. Capturing multiple objects does not produce a transactional device snapshot; inject an `ICaptureValueSource` with the required synchronization if needed. Custom stores and handlers are responsible for their own thread safety.

## Transport and framing

`ApduChannelSettings` holds one physical connection factory and one framing choice. `ApduChannels.ConnectAsync` calls the factory exactly once and returns a ready-to-use `IApduChannel`. Wrapper/TCP example:

```csharp
using EMCL.Core.Objects;
using EMCL.Core.Operations;
using EMCL.Framing;
using EMCL.Framing.Hdlc.Frames;
using EMCL.Messaging;
using EMCL.Protocol.Apdu;
using EMCL.Objects.Definitions;
using EMCL.Transport.Streams;

var settings = new ApduChannelSettings(
    new TcpByteTransportFactory(host, 4059, TimeSpan.FromSeconds(5)),
    new ApduFraming.Wrapper(LocalPort: 16, RemotePort: 1));
await using var channel = await ApduChannels.ConnectAsync(settings, cancellationToken);
await using var endpoint = new DlmsEndpoint(channel);
const byte invokeIdAndPriority = 0xC1;
await endpoint.SendAsync(new LnRequest(invokeIdAndPriority,
    new(CosemOperationKind.Read, CosemClassIds.Register, LogicalName.Parse("1.0.1.8.0.255"), RegisterMembers.Attributes.Value)));
var apdu = await endpoint.ReceiveAsync();
```

For HDLC, replace only `Framing` with `new ApduFraming.Hdlc(HdlcAddress.Client(16), HdlcAddress.Server(1), TimeSpan.FromSeconds(5))`. The channel performs SNRM/UA setup, tracks sequence numbers, acknowledges incoming segments with RR, and sends DISC on disposal. Each outgoing APDU must fit a single negotiated information frame. `HdlcCodec` and `HdlcApduAdapter` are available for low-level frame control.

HDLC information sizes and the receive window can be proposed explicitly:

```csharp
var framing = new ApduFraming.Hdlc(
    HdlcAddress.Client(16), HdlcAddress.Server(1), TimeSpan.FromSeconds(5))
{
    LinkParameters = new HdlcLinkParameters(
        MaximumTransmitInformationBytes: 571,
        MaximumReceiveInformationBytes: 1024,
        ReceiveWindowFrames: 4),
    InformationFrameRetryCount = 1,
    AcknowledgementTimeout = TimeSpan.FromSeconds(2),
    NegotiationObserver = result =>
    {
        // Proposed: local SNRM; Received: peer-direction UA; Effective: local directions.
        Console.WriteLine($"HDLC TX={result.Effective.MaximumTransmitInformationBytes} bytes, "
            + $"RX={result.Effective.MaximumReceiveInformationBytes} bytes, "
            + $"TX window={result.Effective.TransmitWindowFrames} frames, "
            + $"RX window={result.Effective.ReceiveWindowFrames} frames");
    }
};
```

`LinkParameters = null` is the default: SNRM has no information field and local size ceilings are 128 bytes.
Explicit sizes must be 3–2030 bytes, including the LLC header (3 bytes per complete APDU).
Local TX is the smaller of proposed TX and UA RX; local RX is the smaller of proposed RX and UA TX.
An empty UA or an omitted parameter uses the protocol default (128 bytes, one frame), even when larger sizes were proposed.
`Received` is null for an empty UA; its nullable properties preserve omitted individual parameters without displaying
defaults as peer-reported values. `UaInformation` preserves the exact bytes.
`HdlcApduChannel.Negotiation` exposes the latest snapshot; the observer also works through
`ApduChannels.ConnectAsync` and is called again if a parameterized UA replaces an initial empty UA during setup.
The replacement cannot reduce TX below the size of a frame already sent. Incoming I/UI frames exceeding effective RX fail the link.
`TransmitWindowFrames` must remain one; `ReceiveWindowFrames` accepts 1–7 and defaults to one.
UA TX selects the local RX window; UA RX selects the local TX window. A UA window above its matching local proposal is rejected.
With RX > 1, the peer may send a burst of frames: the client acknowledges a continued segmented response with RR
when the peer sets Final, including bursts shorter than the window. Replayed frames are suppressed using a bounded history;
a sequence gap preserves the accepted prefix and requests the missing sequence after Final. Exceeding the negotiated
number of outstanding received frames faults the link. Complete APDUs received before Final are buffered until the turn ends.
UI notifications remain independent and may be delivered between numbered segments.

Window sizes count frames; `AcknowledgementTimeout` separately controls the recovery timer.
For RX > 1 and `InformationFrameRetryCount > 0`, every error-free non-final response frame restarts the receive timer.
Silence during a burst or after its continuation poll triggers another RR with the next expected sequence, without replaying
the application request. Accepted new data resets the receive retry budget; exhausted recovery faults the link.
With retries disabled, a lost Final or continuation poll requires the application's receive deadline to terminate the wait.
Keep a receive loop active while a concurrent `SendAsync` waits for the preceding frame's acknowledgement or the peer's Final;
`DlmsClientSession` already does this. Canceling or failing a windowed receive requires a new connection and releases waiting senders.

These settings do not change `LnAssociationOptions.MaxReceivePduSize`, add outgoing HDLC segmentation,
or enable a larger transmit window. An outgoing APDU plus LLC must still fit one effective TX frame.

Set `InformationFrameRetryCount = 1` and an `AcknowledgementTimeout` on `ApduFraming.Hdlc` to enable one recovery attempt per unacknowledged outgoing I-frame. After the timeout, the channel polls with RR. It sends the same information and send sequence again only if the peer's final RR still expects that sequence. The default retry count is zero. Choose an acknowledgement timeout that allows the initial wait, the recovery poll, and a possible replay within the session's `RequestTimeout`. When enabled, this policy also handles AARQ in place of `ClientSessionOptions.AssociationRetryDelay`. A link that exhausts recovery requires a new connection.

`LinkObserver` reports checksum failures, timeout polls, I-frame retransmissions, discarded out-of-order receive frames,
and receive-window recovery polls. The channel discards frames with bad HCS/FCS and resumes at a validated frame boundary. RNR, REJ, and link reset responses end the exchange with an error.

For a meter that omits the closing `0x7E`, set `AllowMissingClosingFlag = true` on `ApduFraming.Hdlc`. The receiver still requires the opening flag, the declared frame length, and valid HCS/FCS. Outgoing frames include both flags.

For a direct Serial connection, use `SerialByteTransportFactory`:

```csharp
var settings = new ApduChannelSettings(
    new SerialByteTransportFactory(
        new SerialPortSettings("COM3", BaudRate: 9600), TimeSpan.FromSeconds(5)),
    new ApduFraming.Hdlc(HdlcAddress.Client(16), HdlcAddress.Server(1), TimeSpan.FromSeconds(5)));
await using var channel = await ApduChannels.ConnectAsync(settings, cancellationToken);
```

`PortName` accepts an OS port name: `COM3` on Windows, `/dev/ttyUSB0` on Linux, or `/dev/cu.usbserial-...` on macOS. `SerialPortSettings` defines baud rate, data bits, parity, stop bits, flow control, DTR, and RTS; the default is 9600 8N1 without flow control. The channel owns the port and closes it on disposal. Canceling an active read or write also closes the port; open a new connection to continue. An open timeout returns without waiting for a stalled native open; if that open completes later, the port is closed. IEC 62056-21 sign-on and automatic baud-rate switching are not performed. Console template: `examples/EMCL.Examples/meter.serial.example.json`.

For another connection type, implement `IByteTransportFactory.ConnectAsync` to return a custom `IByteTransport`, and pass the factory to `ApduChannelSettings.Connection`. The transport's `ReadAsync` returns the number of bytes received (zero only at EOF), `WriteAsync` sends the entire buffer or throws, and `DisposeAsync` releases the connection. The channel owns the returned transport. For an already open stream, use `StreamByteTransport` and the low-level `ApduChannels.Wrapper`/`ConnectHdlcAsync` methods. If the source already supplies complete APDUs, implement `IApduChannel` directly.

`IByteTransport.WriteAsync` must write the entire buffer or throw. `IApduChannel.ReceiveAsync` returns one complete APDU with bytes owned by the result; `null` means clean EOF. A datagram transport can implement `IApduChannel` directly. The framing channel retains incomplete data between reads, supports multiple frames per read and shared HDLC flags. A truncated frame or segmented message causes `EndOfStreamException`.

After a framing error, the channel rejects further processing with an explicit error; it does not silently resynchronize. Replace the channel or adapter to implement custom recovery. The endpoint remains a low-level tool. `DlmsClientSession` provides association management and response correlation.

## Logical Name client session

`DlmsClientSession` receives an already connected `IApduChannel`: either a direct APDU channel or the result of `ApduChannels.ConnectAsync`. The console example stores connection and framing settings in one channel configuration; the library opens the selected TCP or Serial connection and leaves frame logging to the application. The session lives in the separate `EMCL.Client` assembly, which is independent of framing.

```csharp
using EMCL.Client.Sessions;
using EMCL.Composition;
using EMCL.Core.Objects;
using EMCL.Objects.Definitions;

var builder = new EmclBuilder();
await using var client = new DlmsClientSession(channel);
var negotiated = await client.AssociateAsync(cancellationToken);
var value = await client.GetAsync(
    CosemClassIds.Register, LogicalName.Parse("1.0.1.8.0.255"),
    RegisterMembers.Attributes.Value, cancellationToken: cancellationToken);
var imported = await client.LoadCatalogAsync(
    builder.CreateImporter(), "meter-1",
    "public", AssociationLnMembers.Version3, cancellationToken: cancellationToken);
await client.ReleaseAsync(cancellationToken);
```

To read `object_list` incrementally, call `OpenObjectListAsync(importer, associationVersion)`. It returns a single-use `AssociationObjectStream` after reading the array header: `TotalCount` contains the number of entries advertised by the meter, and `CurrentCount` increases as each entry is yielded through `await foreach`. Dispose the stream with `await using`; completing enumeration verifies that all entries were received and no trailing data remains. `LoadCatalogAsync` uses the same stream and returns a catalog only after full validation.

Example for an open client association, used in place of `LoadCatalogAsync`:

```csharp
await using var objects = await client.OpenObjectListAsync(
    builder.CreateImporter(), AssociationLnMembers.Version3,
    cancellationToken: cancellationToken);
Console.WriteLine($"Advertised objects: {objects.TotalCount}");
await foreach (var entry in objects.WithCancellation(cancellationToken))
{
    Console.WriteLine($"{objects.CurrentCount}/{objects.TotalCount}: {entry.LogicalName}");
}
```

`TotalCount` includes duplicate entries from the meter. Disposing the stream early cancels an incomplete GET and may move the session to `Faulted`; enumerate the stream to completion before starting further operations.

Use `OpenProfileBufferAsync(logicalName, selection)` to stream rows from a Profile Generic buffer. The optional `SelectiveAccess` limits the GET by range or entry. An array reports the selected row count in `TotalCount`. A compact-array reports its content length in `TotalBytes`, with `CurrentBytes` advancing as each decoded row is yielded. `CurrentCount` is available for both formats.

```csharp
await using var profile = await client.OpenProfileBufferAsync(
    LogicalName.Parse("1.0.99.1.0.255"), cancellationToken: cancellationToken);
await foreach (var row in profile.WithCancellation(cancellationToken))
{
    string progress = profile.TotalCount is { } total
        ? $"{profile.CurrentCount}/{total} rows"
        : $"{profile.CurrentBytes}/{profile.TotalBytes} bytes, {profile.CurrentCount} rows";
    Console.WriteLine($"{progress} ({row.Type})");
}
```

Enumerate the profile stream to completion to validate the response. For a block transfer, the client decodes rows without assembling the entire buffer. The exchange still obeys `ProtocolLimits` and the request deadline. Disposing an incomplete read may fault the session.

`SetAsync`, `ActionAsync`, and `ExecuteAsync` are also available. Convenience methods throw `DlmsOperationException` on a meter error; `ExecuteAsync` returns an `LnResponse` with the original result code and a separate ACTION return-data error. `GetAsync` and `ExecuteAsync` assemble successful GET blocks into one A-XDR value. GET-next acknowledges the last received block number. Oversized SET values use service-specific blocks through the same `SetAsync` or `ExecuteAsync` call, only when `BlockTransferWithSet` is proposed and negotiated. Each block includes headers and its A-XDR length determinant in the PDU check; `MaxMessageBytes` bounds the encoded value and `MaxBlockCount` bounds the complete transfer before the first send. Intermediate acknowledgements do not confirm a write: the final result must match the last block. A remote error terminates the transfer and remains a `DlmsOperationException` from `SetAsync`. Catalog import uses an explicitly selected Association LN version; current object values cannot be inferred from the list.

By default, the session executes one request at a time. `MaxConcurrentRequests` permits up to 16 independent exchanges if the meter supports that load. Responses are matched by invoke ID, service, and the full invoke-id-and-priority byte; writes are serialized, and one background reader handles input. `RequestTimeout` covers the entire exchange, including all GET or SET blocks. Waiting for queue capacity is bounded by the caller's cancellation token. `TimeProvider` allows replacing the deadline time source.

Cancellation before sending preserves the association. Cancellation or a timeout after a send attempt, an invalid response, EOF, or exceeded input limits moves it to `Faulted` and completes all other pending operations. This prevents a delayed response from being reused in a new exchange. There are no automatic retries. A SET send attempt without a validated final result throws `DlmsWriteUnconfirmedException`, preserving timeout, cancellation, transport, or protocol failure in `InnerException`. The write may have taken effect; read and reconcile the value before retrying on a new session. Cancellation before any send keeps ordinary cancellation semantics. A session supports one association: after `Released` or `Faulted`, create a new session on an appropriate channel.

`ReleaseAsync` stops accepting new operations, drains existing operations, and performs RLRQ/RLRE. `DisposeAsync` cancels and awaits operations and I/O, then disposes the channel; `LeaveOpen = true` leaves the channel to the application. Custom channels must support read and write cancellation. Repeated disposal is safe. After a failure, reusing a channel left open requires explicit application recovery.

Proposed conformance is checked against the implemented feature set. Services and selective access are enabled only after negotiation. Client and server maximum receive sizes are independent; a protocol value of zero means unlimited, but `ProtocolLimits` always apply. Nonzero sizes below 12 are reserved. Notification and GET block buffers are bounded; the first GET block must be numbered 1. `NotificationCapacity` must not exceed `MaxCollectionItems`, and a nonzero advertised receive size must not exceed `MaxMessageBytes`.

`ReadNotificationsAsync` receives unciphered DataNotification messages independently of pending requests. Enumeration can start as soon as the channel is connected, including before calling `AssociateAsync`. If you associate first, the session buffers notifications received while waiting for AARE, including HDLC UI frames received during SNRM/UA setup. After the session accepts AARE, DataNotification requires negotiated support.

If the meter sends notifications to a separate destination on the same connection, set `ApduFraming.Hdlc.NotificationAddress` or `ApduFraming.Wrapper.NotificationPort` to that destination.

```csharp
await foreach (var notification in client.ReadNotificationsAsync(cancellationToken))
{
    Console.WriteLine($"Push {notification.LongInvokeIdAndPriority:X8}: {notification.Body.Type}");
    // The optional timestamp is in DateTime, and Body contains the decoded DLMS value.
}
```

Run the notification reader alongside any association operations you need. The application must drain the buffer. Overflow terminates the session with an explicit error. Canceling notification enumeration does not cancel the session.

`LnAssociationOptions` supports no authentication, LLS, and HLS High (AES-128 challenge-response) through immutable `Password` bytes. HLS High invokes the Association LN method and validates the response before opening the session. To implement another mechanism, provide `IClientAssociationNegotiator` and `IClientHighLevelAuthenticator`. To replace APDU representation, provide `IIncrementalCodec<DlmsApdu>`. To replace the entire client, implement `IDlmsClientSession`. Protected APDUs require a separate implementation.

A complete runnable example with a local object pipeline is available in [ClientSessionExample.cs](examples/EMCL.Examples/Client/ClientSessionExample.cs). The example's test peer demonstrates the exchange and is not a ready-to-use server session.

## Blocks, limits, and local execution

For GET block transfer, call `GetBlockAssembler.Begin` with a key containing the channel, device, association, and invoke ID. Pass `GetBlockResponse` to `Accept`; after the final block, decode the resulting bytes with `AxdrCodec`. A duplicate or skipped block fails assembly. `Abort` releases an incomplete exchange. Reusing an invoke ID requires a new `Begin`.

`ProtocolLimits` bounds message size, nesting depth, item count, and block count. Defaults: 1 MiB, 64 levels, 100,000 items, and 4096 blocks. When composing components manually, pass the same settings to codecs, assemblers, and the channel. HDLC limits (2047 octets excluding flags) and Wrapper limits (65535 payload octets) apply in addition.

Local handlers:

- Data: reading and writing existing values.
- Register: reading, writing, and resetting a numeric value to zero while preserving its type. For a different reset value, register a `RegisterHandler` with a custom function.
- Extended Register v0: capturing value/status and capture_time, and resetting while preserving status. The class definition specifies the time type.
- Demand Register v0: current/last average, reset/next_period, measurement input, and a bounded sample window. The default calculator returns a Float64 mean with equal sample weights; provide a custom `IDemandValueCalculator` for energy integration, time weighting, and rounding. The application schedules measurement collection and period transitions.
- Register Activation v0: register assignments, creating/replacing/deleting masks, and selecting active_mask. The application calls `IsEnabledAsync` before measuring. Mask indices start at 1, with at most 255 assignments; select another mask before deleting the active one.
- Clock: partial time setting, adjustments, preset, and shift through `IClockSource`. `MemoryClockSource` maintains a private offset from `TimeProvider` without changing the OS clock. This is a local model; the application's time source provides DST scheduling and physical gradual clock adjustment through `ShiftTime`.
- Association LN: visible object list, adding/removing objects, and v3 user management; local server-side HLS processing and HLS secret changes require an external security handler.
- Profile Generic: capture/reset, FIFO/LIFO and four value-based sort orders, capacity, array/compact-array, and range/entry selectors. Changing capture_objects clears the buffer and dependent profiles. The application schedules automatic periodic capture. Abbreviated `null-data` values are not implicitly reconstructed from previous rows.

The detailed capability matrix and sources are in [docs/COVERAGE.md](docs/COVERAGE.md).

Example of a local Extended Register measurement:

```csharp
using EMCL.Objects.Registers;
using EMCL.Objects.Definitions;

var extended = device.Add(CosemClassIds.ExtendedRegister, ExtendedRegisterMembers.Version0, LogicalName.Parse("1.0.3.8.0.255"));
var handler = (ExtendedRegisterHandler)device.Handlers.Resolve(
    device.Catalog.LogicalDeviceId, extended);
var captured = await handler.CaptureAsync(
    extended, DlmsValue.Number(DlmsType.UInt32, 500));
```

The producer API executes locally without checking association permissions and uses the same object lock as the dispatcher. Obtain the handler before installing wrappers or retain a reference to your own instance. A runnable example of all three new classes is in [RegisterExamples.cs](examples/EMCL.Examples/Registers/RegisterExamples.cs).

For meters that transmit time with the date-time tag (25), register Extended/Demand Register definitions using `DlmsType.DateTime`.

## License

The project is distributed under the MIT license. The complete text and copyright notice are in [LICENSE](LICENSE). The license is also included in every library NuGet package.
