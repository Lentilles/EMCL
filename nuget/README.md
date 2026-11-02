# EMCL

An extensible DLMS/COSEM library for .NET 10: Logical Name client sessions,
a COSEM object model, binary codecs, and independent transport adapters.

## Installation

```sh
dotnet add package EMCL --prerelease
```

For reproducible builds, specify the selected published version with `--version`.
The `EMCL` package references all library components. Individual components can
also be installed separately:

| Package | Purpose |
| --- | --- |
| EMCL | Composition of objects, Wrapper/HDLC client channels, and endpoints |
| EMCL.Core | DLMS values, identifiers, descriptors, and extension contracts |
| EMCL.Protocol | A-XDR, ACSE, LN APDUs, and GET/SET block transfer |
| EMCL.Client | LN associations, GET/SET/ACTION, notifications, and catalog reading |
| EMCL.Objects | COSEM catalog, association import, and local handlers |
| EMCL.Framing.Hdlc | HDLC type 3, CRC, LLC, and segmentation |
| EMCL.Framing.Wrapper | Wrapper v1 |
| EMCL.Transport | TCP, Serial, Stream, and replaceable APDU channels |

## Local execution example

```csharp
using EMCL.Composition;
using EMCL.Core.Objects;
using EMCL.Core.Operations;
using EMCL.Core.Values;
using EMCL.Objects.Definitions;

var device = new EmclBuilder().Build("meter-1");
var register = device.Add(
    CosemClassIds.Register, RegisterMembers.Version0,
    LogicalName.Parse("1.0.1.8.0.255"));
register.State.Set(RegisterMembers.Attributes.Value,
    DlmsValue.Number(DlmsType.UInt32, 12345));

var result = await device.Pipeline.ExecuteAsync(
    new(CosemOperationKind.Read, CosemClassIds.Register,
        register.LogicalName, RegisterMembers.Attributes.Value),
    device.LocalContext);
Console.WriteLine(result.Value!.AsUInt64());
```

## Supported features

- GET/SET/ACTION normal, selective access, automatic GET block assembly, and negotiated SET block transfer.
- Association LN v1/v3: catalog import and streaming `object_list` reading.
- None, LLS, and HLS High (AES-128 challenge-response) authentication.
- Wrapper and HDLC over TCP, Serial, or a custom transport.
- Local Data, Register, Extended Register, Demand Register, Register Activation,
  Clock, Profile Generic, and Association LN, as well as custom classes.
- Preservation of unknown classes and versions, original DLMS tags, and association rights.
- XML API documentation and separate `.snupkg` debug symbol packages.

Short Name, protected APDUs, HLS SHA-256/GMAC, ACTION block transfer, GBT,
and a ready-to-use server session are not implemented yet. Each outgoing HDLC APDU
must fit a single negotiated information frame. Serial starts exchanging data
at the configured baud rate; IEC 62056-21 sign-on is not performed.

`EMCL.Transport` uses `System.IO.Ports`. The other library projects have
no external NuGet dependencies beyond EMCL components.

## License

MIT. Copyright (c) 2026 Alexey Tyapkin. The complete text is in the `LICENSE` file
inside the package.
