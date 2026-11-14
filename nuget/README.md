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
| EMCL.Protocol | A-XDR, ACSE, LN APDUs, block transfer, and pluggable security |
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

- GET/SET/ACTION, selective access, service-specific block transfer, and General Block Transfer.
- Association LN v1/v3: catalog import and streaming `object_list` reading.
- None, LLS, HLS High, HLS GMAC and HLS Kuznyechik CTR/CMAC. Custom security protocols can implement `ILnSecurityProtocol`.
- Selectable COSEM, SPODES 4 and preliminary SPODES 5 profiles. The SPODES profiles recognize 26 and 57 exact class versions respectively.
- Confirmed and unconfirmed Push on associated clients or pre-established channels, including Wrapper/UDP.
- Wrapper and HDLC over TCP, Serial, or a custom transport.
- Optional HDLC MAX INFO TX/RX negotiation with asymmetric byte limits and separate proposed, received, and effective diagnostics; receive windows of 1–7 frames and a one-frame transmit window.
- Local Data, Register, Extended Register, Demand Register, Register Activation,
  Clock, Profile Generic, and Association LN, as well as custom classes.
- Preservation of unknown classes and versions, original DLMS tags, and association rights.
- XML API documentation and separate `.snupkg` debug symbol packages.

Each outgoing HDLC APDU must fit a single negotiated information frame. Serial starts exchanging data at the configured baud rate. Server applications supply the session and connect their device adapters.

`EMCL.Transport` uses `System.IO.Ports`. `EMCL.Protocol` uses `BouncyCastle.Cryptography`.

## License

MIT. Copyright (c) 2026 Alexey Tyapkin. The complete text is in the `LICENSE` file
inside the package.
