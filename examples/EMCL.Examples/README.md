# EMCL console examples

The application reads Association LN `object_list` from a meter over **Wrapper/TCP**, **HDLC through a TCP gateway**, or **Serial**, and saves JSON. It passes unified channel settings to `ApduChannels.ConnectAsync`; the library opens the selected connection once and applies Wrapper or HDLC framing. Connection and export settings are stored in a separate file. The application performs association and GET; it does not request other attribute values.

## Running

Copy the template to `meter.local.json`, fill in the address, ports, and association settings, then run the `EMCL.Examples` project from the IDE with the usual **Run** command and no arguments. From a command line at the repository root:

```sh
cp examples/EMCL.Examples/meter.example.json examples/EMCL.Examples/meter.local.json
dotnet run --project examples/EMCL.Examples -c Release
```

With no arguments, the application uses `meter.local.json` if present, otherwise `meter.example.json`. Both files are copied next to the application during the build, so the IDE working directory does not affect configuration selection. After export, the console prints the full JSON path. Before running, configure the meter settings: `channel.connection` selects the connection type (`"kind": "Tcp"` or `"kind": "Serial"`) and its parameters; omitting `kind` selects TCP. `channel.framing` selects `"kind": "Wrapper"` with `clientPort`/`serverPort`, or `"kind": "Hdlc"` with `clientAddress`/`serverLogicalAddress` and optional `serverPhysicalAddress`. Numbers are decimal. The legacy `connection.transport: WrapperTcp/HdlcTcp` format is also accepted and converted to the new settings during loading.

For Serial, copy `meter.serial.example.json` to `meter.local.json` and set `portName` and the meter's line parameters. Port names include `COM3` on Windows, `/dev/ttyUSB0` on Linux, and `/dev/cu.usbserial-...` on macOS. Example channel settings:

```json
{
  "connection": {
    "kind": "Serial",
    "portName": "COM3",
    "baudRate": 9600,
    "dataBits": 8,
    "parity": "None",
    "stopBits": "One",
    "handshake": "None",
    "dtrEnable": false,
    "rtsEnable": false,
    "connectTimeoutSeconds": 10
  },
  "framing": {
    "kind": "Hdlc",
    "clientAddress": 16,
    "serverLogicalAddress": 1
  }
}
```

This object replaces the `channel` section. Allowed values: `parity` — `None`, `Odd`, `Even`, `Mark`, `Space`; `stopBits` — `One`, `Two`, `OnePointFive`; `handshake` — `None`, `XOnXOff`, `RequestToSend`, `RequestToSendXOnXOff`. Support for individual modes depends on the OS and driver. With hardware flow control, the driver manages RTS, so leave `rtsEnable: false`. Defaults: 9600 8N1, no flow control, DTR and RTS disabled. Binary HDLC normally requires `handshake: None` so XON/XOFF bytes remain data.

The port closes when the channel is disposed, an active read/write is canceled, or an exchange times out. A new connection is then required. A direct connection starts HDLC exchange immediately; IEC 62056-21 sign-on and automatic baud-rate switching are not performed.

For HDLC server address `1/17`, the default encoding uses two bytes, `02 23`. For the four-byte form `00 02 00 23`, set `"extendedAddress": true` in `channel.framing`. In the legacy configuration format, this field is named `"hdlcExtendedAddress": true` and belongs to `connection`. Both forms represent the same logical and physical addresses; the length is shown in the connection description.

The console shows communication: complete DLMS APDUs for Wrapper, and SNRM/UA/I/RR frames for HDLC. Each message receives a number, a `TX`/`RX` direction, a type, a length, and hexadecimal bytes. For example, `[0001] TX Aarq (42 bytes): 6028...`. With HDLC, the full AARQ frame is printed, including authentication data; review such logs before sharing them. Authentication ACTION frames and Wrapper AARQ messages remain hidden. Individual byte-stream reads do not increment the counter.

After AARE, the application shows the number of `object_list` entries advertised by the meter, then prints each validated entry with its number, OBIS code, class, and version as GET blocks arrive. The final JSON is saved only after the entire list has been received and validated.

To use another configuration file, pass `--config /path/to/meter.json`. A relative `output.path` is resolved against the directory containing the selected configuration. When running from the IDE, this is the application's build directory. For example, `"path": "exports/meter.json"` creates an `exports` subdirectory there. The template sets `overwrite: true`, so a repeated run updates the JSON; set it to `false` to preserve an existing file. JSON is written to a temporary file and published by renaming after serialization completes; cancellation and errors do not leave a partial destination file.

Set the Association LN version explicitly in `association.version` (1 or 3); it must match the meter's actual association. It determines how access rights are interpreted. `logicalName` selects the association object. `logicalDeviceId` and `associationId` are local names for the catalog and permission snapshot, not connection addresses.

`connectTimeoutSeconds` limits TCP connection establishment or Serial port opening; `requestTimeoutSeconds` covers the entire exchange, including all GET blocks. `limits` bounds size, nesting, collections, and block count. Reading requires only `"conformance": "Get, BlockTransferWithGet"`.

To reproduce the exact bit set from another AARQ, set `"advertisedConformanceHex": "621EDF"` in `association`. This changes the 24-bit proposed Conformance in AARQ. The `conformance` field still lists the operations implemented by the client; each of its bits must be included in the proposed mask. Named flags for other services are available in `DlmsConformance`, but their operations are not implemented yet.

## Authentication

For a public association, use `"authentication": "None"` and leave all password fields set to `null`.

For LLS, set `"authentication": "Low"` and use **exactly one** source:

- `password`: text encoded as UTF-8;
- `passwordHex`: exact bytes in hexadecimal without separators;
- `passwordEnvironmentVariable`: the name of an environment variable containing a UTF-8 text password.

Example of an environment variable reference:

```json
{
  "authentication": "Low",
  "password": null,
  "passwordHex": null,
  "passwordEnvironmentVariable": "EMCL_METER_PASSWORD"
}
```

For the HLS `High` mechanism (AES-128 challenge-response compatible with Gurux), set `"authentication": "High"`, provide one of the same sources for a 16-byte secret, and include `Action` in `conformance`, for example `"Get, Action, BlockTransferWithGet"`. After AARE, the client invokes `reply_to_HLS_authentication` on Association LN and validates the response before opening the session. Other HLS mechanisms, including SHA-256 and GMAC, are not implemented yet.

Place this fragment inside the full configuration's `association` section. Passwords are not printed to the console or included in the export. For a password consisting of digits, ASCII and UTF-8 produce identical bytes. AARE diagnostics are printed as a number and name, for example `diagnostic=11 (AuthenticationMechanismNameNotRecognized)`; code 11 alone does not identify the mechanism required by the meter.

## Export contents

The root contains `schemaVersion` (currently 3), `exportedAtUtc`, `source`, negotiated parameters in `negotiated`, `objectCount`, `objects`, and `warnings`. `source` records the selected transport and its corresponding addresses. For Serial, the transport is `HdlcSerial` or `WrapperSerial`, port settings are stored in `source.serial`, and `host`/`tcpPort` are `null`.

If the meter returns exactly the same `object_list` entry more than once, you can enable `"ignoreIdenticalObjectListEntries": true` in the `association` section. The export keeps the unique entry and adds a warning to `warnings`. Entries with the same OBIS code but different contents still cause an import error.

For each object, the export includes:

- `obisCode` in `A.B.C.D.E.F` form, `classId`, `classVersion`, `className`, and `definitionKnown`;
- all attributes and methods reported by the meter, supplemented with members of the known exact class definition;
- indices, known names, and DLMS types/tags for attributes and method parameters;
- `reportedByAssociation` and `definedByLibrary` to distinguish meter data from library metadata;
- `access.rawMode`, its interpretation in `encoding`, readable `modes` names, and `read`/`write` or `invoke` flags;
- v1 authentication requirements or separate v3 request/response protection flags, plus unknown bits in `unknownBits`;
- attribute selectors reported by the association.

`access: null` means that the meter did not report access rights for that member. This differs from an explicitly reported no-access mode, `rawMode: 0`. Unknown classes, versions, and additional members retain their identifiers and rights; unknown names and types are `null`. Placeholder names from the importer are not presented as standard definitions. A CHOICE attribute's type may also remain `null`.

Objects are sorted by numeric OBIS code, and members by signed index. Negative custom indices are preserved.

After saving, the application attempts a graceful association release. If the meter does not acknowledge release, the completed JSON is retained and a diagnostic message is printed to stderr. Main read/import/write errors return code `1`, invalid arguments return `2`, and Ctrl+C returns `130`.

## Local demonstrations

```sh
dotnet run --project examples/EMCL.Examples -c Release -- --demo
dotnet run --project examples/EMCL.Examples -- --help
```

`--help` prints usage information. `--demo` runs the existing local examples, including a client session with a test peer; no meter connection is required.
