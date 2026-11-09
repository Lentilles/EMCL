# Capability matrix

| Feature | Implementation |
| --- | --- |
| Logical Name | Supported; signed attribute and method indices, including custom negative indices |
| Short Name | Future work |
| Data 1/v0, Register 3/v0 | Standard definitions |
| Extended Register 4/v0 | Value/status capture, reset, and capture_time through TimeProvider |
| Demand Register 5/v0 | reset/next_period, samples and a period × number_of_periods window, replaceable calculation |
| Register Activation 6/v0 | add_register/add_mask/delete_mask, mask selection, and register enablement checks |
| Clock 8/v0 | Configurable time type and time-zone bounds; local clock source |
| Profile Generic 7/v1 | Descriptor, capture/reset, sort methods 1–6, capacity, selective access 1/2 |
| Association LN 15/v3 | Supported |
| Association LN 15/v1 | Supported |
| Other classes and versions | Generic objects from the association; users provide definitions and behavior |
| A-XDR Data | Null, array, structure, boolean, bit-string, all integer/float types, enum, BCD, strings, date/time, and delta types |
| Compact-array | Type description, nested structures and arrays, row reading/writing without repeated tags |
| GET/SET/ACTION | Normal request/response; GET and SET with selective access |
| GET block transfer | GET-next, response-with-datablock, separate assembly with an explicit lifecycle |
| SET block transfer | First/subsequent data block requests, block acknowledgements, and final block results |
| DataNotification | Codec for long invoke ID, optional time, and Data |
| ACSE | AARQ/AARE/RLRQ/RLRE, definite-length BER, preservation of fields including authentication fields |
| xDLMS negotiation | InitiateRequest/InitiateResponse and conformance bit-string |
| BER indefinite length / high-tag-number fields | Not supported; returns an error or Unsupported |
| HDLC | Type 3, 1/2/4-octet addresses, control byte, HCS/FCS, shared flags, I-frame segmentation, and opt-in reception without a closing flag |
| LLC | Command/response header and broadcast destination |
| Wrapper | Version 1, wPorts, 16-bit length |
| Stream | Adapter and fragmented I/O, leaveOpen, cancellation |
| TCP lifecycle | `TcpByteTransport` and factory: opening with a timeout, cancellation, and connection ownership |
| Serial lifecycle | `SerialByteTransport` and factory: line settings, opening with a timeout, binary I/O, closing on cancellation, and port ownership |
| UDP lifecycle | Provided by the application or a custom transport |
| IEC 62056-21 sign-on | Not supported; Serial starts exchanging data immediately at the configured baud rate |
| Logical Name client session | AARQ/AARE, conformance and independent PDU limits, RLRQ/RLRE, states, and channel ownership |
| Client operations | GET/SET/ACTION normal, selective access, up to 16 invoke IDs, one reader and a serialized writer |
| Client GET block transfer | Automatic GET-next acknowledging the received block number; bounded assembly and validation of one A-XDR value |
| Client SET block transfer | Automatic first/subsequent data blocks; negotiated support, complete PDU bounds, sequential acknowledgements, and a final data-access result |
| Console export | Wrapper/TCP and HDLC/TCP gateway (SNRM/UA, I/RR without retries), separate JSON configuration, None/LLS/HLS High, OBIS/classes/members/v1/v3 rights in JSON; atomic saving |
| Client catalog loading | Reading object_list, including GET blocks, and atomic import with an explicit Association LN version |
| Client Profile Generic reading | Streaming array and compact-array buffer rows from normal and block GET responses, with selective access and progress |
| Client notifications | Separate bounded stream of negotiated DataNotification messages; overflow terminates the session |
| Deadlines and cancellation | Cover the entire exchange; cancellation after a send attempt faults the association, while queue waiting is canceled separately |
| HDLC size negotiation | Optional SNRM MAX INFO TX/RX proposals (3–2030 bytes including LLC); asymmetric UA limits, separate proposed/received/effective snapshots, incoming I/UI size enforcement; windows fixed at one frame |
| HDLC recovery | Optional RR polling and I-frame replay after an acknowledgement timeout, checksum recovery and duplicate suppression |
| Ready-to-use server session, full HDLC link management | Future work |
| ACTION block transfer, GBT, APDU list variants | Future work; Unsupported during decoding |
| Client session authentication | None, LLS, and HLS High through AES-128 challenge-response and Association LN ACTION; AARE response and diagnostic are validated |
| Protected APDUs, HLS SHA-256/GMAC, and other mechanisms | Require an external implementation; successful authentication is not simulated |

## Model boundaries

Access rights are snapshots of a particular association, not class properties. Association remove_object changes visibility within one association. Converters and handlers must not independently interpret a v1 enum as a v3 bitmask.

`object_list` contains neither values nor complete schemas for unknown classes. A descriptor from another version is not substituted for an unknown version. After registering a definition, you can explicitly call `ObjectCatalog.Rebind`.

Date and time formats are preserved without changing tags. In particular, Clock.time is an octet-string in the Blue Book and date-time in the supplied SPODES 4 document. Original special fields remain available as bytes; conversion to DateTimeOffset requires a concrete date and deviation.

Extended/Demand Register uses a 12-octet octet-string in the Blue Book and date-time (tag 25) in SPODES. Tables 7.3.3–7.3.4 in the supplied SPODES 4 document list the number 27 next to the name 'date-time'. The data types section and Clock specify tag 25 for date-time, while tag 27 denotes a four-octet time. The library defaults to octet-string; for tag 25, register a class definition with `DlmsType.DateTime`.

The local Extended Register captures value and time through `CaptureAsync`; reset updates the time while preserving status. Demand Register accepts samples through `RecordSampleAsync`. The default calculator returns a Float64 mean with equal weight for each sample, regardless of the interval between samples. It does not integrate energy or reconstruct values from delta tags: use `IDemandValueCalculator` for those calculations, time weighting, and rounding. The application supplies values in consistent scaler/unit units. The retention window is `period × number_of_periods`; samples at the lower boundary expire. On `next_period`, the current result becomes the last result; samples are cleared for a single period and retained for a sliding window. Changing period/number_of_periods or writing an average clears samples while preserving other attributes. Sample history lives in the handler and is not restored from storage after a restart. The application schedules periodic collection and next_period calls.

Register reset requires an initial numeric value to determine the zero type, or an injected default-value function. Unpopulated attributes remain absent; known standard defaults are `Demand Register.number_of_periods = 1` and empty Register Activation lists. Handlers serialize local measurement input with dispatcher operations. A batch of writes assumes that `IObjectStateStore.Set` succeeds; an arbitrary external store must provide its own transactions.

Register Activation checks that assigned objects of classes 3/4/5 exist, mask names are unique, and indices fall between 1 and the length of register_assignment. This implementation supports up to 255 assignments. Repeating add_mask with the same byte name replaces the mask; add_register appends an assignment while preserving positions. Deleting the selected mask returns TemporaryFailure: select another mask first. An empty byte name is valid. Unassigned registers are enabled; assigned registers without a selected mask are disabled. The application uses `IsEnabledAsync` when collecting measurements; the handler does not block arbitrary register state writes. If an assigned object is removed directly from the catalog, the application must reconcile the configuration afterward.

A codec input buffer may contain multiple messages; `Consumed` indicates the end of the first message. Errors and NeedMoreData return zero consumption. HDLC retains the final flag because it may open the next frame. LLC receives an already complete information message.

Profile Generic stores row values explicitly. Compact representation is preserved during loading without reordering or truncation; capture or changing the order may convert the buffer to a regular array. Selector 2 entry/column indices start at 1; an end index of 0 means through the end. Failed operations must not append a partial capture row.

## Client session

`EMCL.Client` depends on Core, Protocol, Objects, and Transport. It does not depend on HDLC or Wrapper. ACSE construction/validation policies and the APDU codec can be replaced explicitly; `IDlmsClientSession` allows replacing the entire client.

The session is single-use and does not establish a transport connection. Invalid correlation, a duplicate response, a disconnect, an unsupported message, exceeded PDU/GET limits, or cancellation after a send attempt terminates the association. Meter errors are preserved as remote operation errors and allow further exchanges. GET and SET blocks start at 1; order and the full invoke octet are checked separately for each request. SET needs a final block response with a result; an early block result or normal SET error aborts the transfer. No SET or block is retried. Missing or invalid confirmation after any SET send attempt raises DlmsWriteUnconfirmedException with the original cause; even a timeout or caller cancellation cannot establish whether the write took effect. ACTION block transfer and GBT are not proposed during negotiation.

If the peer closes after delivering the final GET block, the association faults but
the completed response can finish local decoding and catalog export. Caller
cancellation and the exchange deadline still apply; a transfer without a final
block fails. Console progress and frame traces share synchronized output writers
so concurrent writes preserve complete log lines.

Release stops accepting new operations and drains existing ones. Dispose cancels operations, waits for reads/writes, and disposes the channel according to LeaveOpen. Custom channels must honor the cancellation contract for shutdown to complete correctly. An operation deadline covers all of its GET or SET blocks; the release deadline includes waiting for in-flight operations.

## Verification

After a Release build, run: `dotnet test --project tests/EMCL.Tests/EMCL.Tests.csproj -c Release --no-build --no-restore`.

xUnit v3 tests verify reference bytes for A-XDR, BER/ACSE, LN APDUs, and Wrapper; the independent CRC-16/X-25 check value `123456789 → 0x906E`; SNRM `7E A0 07 03 21 93 0F 01 7E`; every incomplete message prefix; bounded buffers; malformed input; exact class versions; v1/v3 rights; import; local behavior; sorting; extensions from another assembly; and equivalence of three APDU transmission paths. Reference vectors and sort orders use parameterized tests; transport resource ownership under repeated disposal is also checked.

These checks cover the implemented feature set described here; DLMS UA certification testing and validation against physical meters are outside the project scope.

For classes 4/5/6, tests cover exact-version import and definition replacement, timestamp types, status preservation, reset/next_period, sliding windows and sample expiration, numerical stability of the mean, custom calculators, limits, cancellation, time moving backward, atomic validation failures, mask references/indices, binary GET/ACTION, and capture into Profile Generic.

Client tests exercise the full association, GET/SET/ACTION, catalog loading, and release lifecycle through a direct APDU channel and Wrapper with fragmented bytes. They check reversed concurrent responses, independent GET blocks and reference GET-next messages, rejected AARE and invalid parameters, separate PDU limits and zero-as-unlimited, LLS, notifications and overflow, meter errors and ACTION return-data, SET PDU boundaries and length determinants, negotiated SET blocks, early/final SET errors, corrupt acknowledgements, lost final SET confirmation, transfer resource bounds, controlled timeouts, cancellation during queue waiting/after sending/during association, Dispose with pending requests, and LeaveOpen. A separate test project injects a custom codec and ACSE strategy.

## Sources

The implementation was checked against the supplied local documents:

- Blue Book Edition 16 Part 2, Version 1.0, October 30, 2023: 4.1 (model and types), 4.3.1–4.3.5 (Data and registers), 4.3.6 (Profile Generic), 4.4.4 (Association LN), and 4.5.1 (Clock).
- STO 34.01-5.1-006-2023, version 4 (SPODES): 6.4 (addressing), 7.2 (data), 7.3.1–7.3.7, and the Association LN description.
- Green Book Edition 11, Version 1.0, December 21, 2021: sections 7–8 (Wrapper, HDLC/LLC), section 9 and ACSE/xDLMS ASN.1 definitions; 9.1.4.7–9.1.4.8 (conformance and PDU), 9.3.2–9.3.3 (association and release), the GET and SET block transfer descriptions (including 9.4.6.4 and Figure 124), and the SET ASN.1/XSD definitions.

The PDFs themselves are not copied into the repository.

The console example is tested through a real TCP loopback connection and Wrapper: reading object_list across multiple GET blocks, v1/v3 rights, saving JSON, and disconnecting after the final block. A separate HDLC/TCP test checks SNRM/UA, I-frame exchange, segment assembly, and RR. Other xUnit tests cover configuration, password sources, absence of passwords in exports, unknown classes/members and null for unreported rights, protection flags/selectors, cancellation, and preservation of existing files.

Serial tests cover line settings, configuration and export, binary byte transfer, idempotent disposal, I/O cancellation with a stream that ignores CancellationToken, cleanup errors for a disconnected device, open timeouts, and port disposal after a late open completion. These tests require no physical device.
