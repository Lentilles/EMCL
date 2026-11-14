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
| PPP Setup 44/v0, Communication Port Protection 124/v0 | Blue Book definitions in all three profiles |
| SPODES profiles | 26 exact class versions in SPODES 4 and 57 in the supplied SPODES 5 draft |
| Other classes and versions | Generic objects retain their association identifiers and rights; definitions and handlers are extensible |
| A-XDR Data | Null, array, structure, boolean, bit-string, all integer/float types, enum, BCD, strings, date/time, and delta types |
| Compact-array | Type description, nested structures and arrays, row reading/writing without repeated tags |
| GET/SET/ACTION | Normal request/response; GET and SET with selective access |
| GET block transfer | GET-next, response-with-datablock, separate assembly with an explicit lifecycle |
| SET block transfer | First/subsequent data block requests, block acknowledgements, and final block results |
| Push | DataNotification and DataNotificationConfirm codecs, capture-list binding, and associated/pre-established channels |
| ACSE | AARQ/AARE/RLRQ/RLRE, definite-length BER, preservation of fields including authentication fields |
| xDLMS negotiation | InitiateRequest/InitiateResponse and conformance bit-string |
| BER indefinite length / high-tag-number fields | Not supported; returns an error or Unsupported |
| HDLC | Type 3, 1/2/4-octet addresses, control byte, HCS/FCS, shared flags, I-frame segmentation, and opt-in reception without a closing flag |
| LLC | Command/response header and broadcast destination |
| Wrapper | Version 1, wPorts, 16-bit length |
| Stream | Adapter and fragmented I/O, leaveOpen, cancellation |
| TCP lifecycle | `TcpByteTransport` and factory: opening with a timeout, cancellation, and connection ownership |
| Serial lifecycle | `SerialByteTransport` and factory: line settings, opening with a timeout, binary I/O, closing on cancellation, and port ownership |
| UDP channel | Wrapper datagrams over an application-owned connected socket |
| IEC 62056-21 sign-on | Not supported; Serial starts exchanging data immediately at the configured baud rate |
| Logical Name client session | AARQ/AARE, conformance and independent PDU limits, RLRQ/RLRE, states, and channel ownership |
| Client operations | GET/SET/ACTION normal, selective access, normal or high request priority, up to 16 invoke IDs, and sequential compatibility options for a fixed request octet or ignoring response Invoke-Id-And-Priority |
| Client GET block transfer | Automatic GET-next acknowledging the received block number; bounded assembly and validation of one A-XDR value |
| Client SET block transfer | Automatic first/subsequent data blocks; negotiated support, complete PDU bounds, sequential acknowledgements, and a final data-access result |
| Console export | Wrapper/TCP and HDLC/TCP gateway (SNRM/UA, I/RR without retries), separate JSON configuration, None/LLS/HLS High, OBIS/classes/members/v1/v3 rights in JSON; atomic saving |
| Client catalog loading | Reading object_list, including GET blocks, and atomic import with an explicit Association LN version |
| Client Profile Generic reading | Streaming array and compact-array buffer rows from normal and block GET responses, with selective access and progress |
| Client notifications | Bounded stream with application confirmation and protection; overflow terminates the session |
| Deadlines and cancellation | Cover the entire exchange; cancellation after a send attempt faults the association, while queue waiting is canceled separately |
| HDLC size negotiation | Optional SNRM MAX INFO TX/RX proposals (3–2030 bytes including LLC); asymmetric UA limits, separate proposed/received/effective snapshots, incoming I/UI size enforcement; TX window fixed at one frame |
| HDLC receive windows | Negotiated RX windows 1–7, Final-driven RR acknowledgements, partial bursts, modulo-eight wrap, bounded duplicate suppression and poll/final sequence-gap recovery; TX remains one frame |
| HDLC recovery | Optional RR polling and I-frame replay after an acknowledgement timeout; receive-window silence/continuation recovery, checksum recovery and duplicate suppression |
| Ready-to-use server session, full HDLC link management | Future work |
| ACTION block transfer | Request and response pblocks with negotiated support, protected PDU limits and unconfirmed outcome reporting |
| GBT | Bounded receive and stop-and-wait transmission; supported for Push and oversized client SET/ACTION |
| APDU list variants | Future work |
| Client session authentication | None, LLS, and HLS High through AES-128 challenge-response and Association LN ACTION, with an optional HLS-specific Invoke-Id-And-Priority octet; AARE response and diagnostic are validated |
| Protected LN | Built-in AES-GCM/GMAC (suite 0, HLS ID 5) and Kuznyechik CTR/CMAC (suite 3, HLS ID 128); injectable through `ILnSecurityProtocol` |

## SPODES interface classes

Each listed key is tested through local creation and Association LN import against independent SPODES 4 and 5 lists. The last column identifies additional behavioral verification or the boundary of the hardware interface. The SPODES 5 profile follows the supplied preliminary edition.

| Class ID and name | Version 4 / version 5 | Definition source | Beyond creation and import |
| --- | --- | --- | --- |
| 1 Data, 3 Register | 0 / 0 | SPODES registers and Blue Book | Existing read, write and register reset tests |
| 4 Extended Register, 5 Demand Register, 6 Register Activation | 0 / 0 | SPODES registers and Blue Book | Capture, period, mask and time-tag tests |
| 7 Profile Generic | 1 / 1 | SPODES and Blue Book | Buffer, capture and selector tests |
| 8 Clock | 0 / 0 | SPODES clock tables and Blue Book | Time-tag tests and existing clock behavior tests |
| 9 Script Table | 0 / 0 | SPODES and Blue Book | Nested execution, cycle and operation failure tests |
| 10 Schedule, 11 Special Days Table | 0 / 0 | SPODES and Blue Book | Special day insertion in the local demo; schedule evaluation uses the automation service |
| 15 Association LN | 1 / 1 | SPODES and Blue Book | Import, visibility and access-right tests |
| 17 SAP Assignment | 0 / 0 | SPODES 4 Appendix E, SPODES 5 and Blue Book | Local in-memory assignment |
| 18 Image Transfer | 0 / 0 | SPODES and Blue Book | Out-of-order blocks, resuming an interrupted upload in the same process, verification and activation |
| 19 IEC Local Port Setup, 23 IEC HDLC Setup | 1 / 1 | SPODES port tables and Blue Book | Application adapter |
| 20 Activity Calendar, 21 Register Monitor, 22 Single Action Schedule | 0 / 0 | SPODES and Blue Book | Monitor threshold and Single Action Schedule tests; Activity Calendar uses local automation |
| 29 Auto Connect | — / 2 | SPODES 5 and Blue Book | Application adapter |
| 40 Push Setup | 2 / 3 | SPODES Push tables and Blue Book | Confirmation after retry, capture binding, and Wrapper GBT exchange tests |
| 41 TCP/UDP Setup | 0 / 0 | SPODES communication profile and Blue Book | Application adapter |
| 42 IPv4 Setup, 43 MAC Address Setup, 45 GPRS Modem Setup | — / 0 | SPODES 5 and Blue Book | Application adapter |
| 44 PPP Setup | 0 / 0 | Blue Book | Application adapter |
| 47 GSM Diagnostic | — / 2 | SPODES 5 and Blue Book | Application adapter |
| 48 IPv6 Setup | — / 0 | TCP/UDP Setup reference and Blue Book | Application adapter |
| 64 Security Setup | 1 / 1 | SPODES security tables and Blue Book | Key-transfer staging and application key-store contract |
| 68 Arbitrator | 0 / 0 | SPODES and Blue Book | Local weighted arbitration |
| 70 Disconnect Control | 0 / 2 | SPODES control modes and delayed disconnect procedure, Blue Book | Restart checkpoint and adapter refusal tests; method indices 129–131 use wire octets 81–83 hex |
| 71 Limiter | 0 / 0 | SPODES and Blue Book | Local observation and emergency profile evaluation |
| 80 LLC SSCS Setup, 81 PRIME PHY Counters, 82 PRIME MAC Setup, 83 PRIME MAC Parameters | — / 0 | SPODES 5 and Blue Book | Application adapter |
| 84 PRIME MAC Counters, 85 PRIME Network Administration, 86 PRIME Application Identification | — / 0 | SPODES 5 and Blue Book | Application adapter |
| 90 G3-PLC MAC Counters | — / 1 | SPODES 5 and Blue Book | Application adapter |
| 91 G3-PLC MAC Setup, 92 G3-PLC Adaptation Setup | — / 4 | SPODES 5 and Blue Book | Application adapter |
| 101 ZigBee Startup, 102 ZigBee Join, 103 ZigBee Fragmentation, 104 ZigBee Network Control, 105 ZigBee Tunnel Setup | — / 0 | SPODES 5 and Blue Book | Application adapter |
| 124 Communication Port Protection | 0 / 0 | Blue Book | Application adapter |
| 128 LoRaWAN Setup, 129 LoRaWAN Diagnostic | — / 0 | SPODES 5 | Application adapter; nested modem parameters need independent wire fixtures |
| 151 LTE Monitoring | — / 1 | SPODES 5 and Blue Book | Application adapter |
| 5240 Extended GSM Settings, 5241 Extended GSM Diagnostics | — / 1 | SPODES 5 | Application adapter |
| 5242 RF Setup, 5243 RF Diagnostics, 5244 Communication Module Setup, 5245 Communication Module Diagnostics, 5246 Extended SIM Settings | — / 0 | SPODES 5 | Application adapter |

### Implementation boundaries

- Push keeps one pending message per local Push Setup object, with communication windows, random delay and retries. It captures whole attributes or indexed elements. Restricted captures, column projections, the shared edition-specific queue and clearing event flags on confirmation remain open. The application supplies the transport and initiates periodic processing.
- The GBT sender uses a one-block window. The receiver checks sequence and requests recovery at the last contiguous block; timeout-driven block retransmission and a full streaming recovery exchange remain open. Dedicated Push, protected associations and long ACTION have in-memory Wrapper test peers. Existing HDLC regression tests cover the link and notification reception. UDP and NIDD integration remain unverified against external peers.
- Image data lives in memory until activation. Disconnect Control stores a remaining-time checkpoint through the object state store. The application supplies durable state and periodically calls the automation service for checkpointing. Clock transitions outside concrete or partially specified calendar dates require application handling.
- Network and diagnostic definitions validate operation values and delegate to `ISpodesDeviceAdapter`. An absent adapter returns `ObjectUnavailable`. The application integrates physical devices, server-side association security and the callback that applies successful security changes after its ACTION response.
- Symmetric protected LN and key wrapping use application-owned keys and counter persistence. Tests verify both built-in suites over Wrapper, a Green Book GMAC vector and RFC 3394 AES Key Wrap. Independent Kuznyechik vectors cover the block cipher, CTR, CMAC and the base key export algorithm. The SPODES key transfer framing and 12-byte nonce also have round-trip and replay tests. [RFC 7801](https://www.rfc-editor.org/rfc/rfc7801.html) · [GOST R 34.13-2015, Appendix A](https://meganorm.ru/Data2/1/4293762/4293762703.pdf) · [RFC 9189, Appendix A](https://www.rfc-editor.org/rfc/rfc9189.html#appendix-A)

## Model boundaries

Access rights are snapshots of a particular association, not class properties. Association remove_object changes visibility within one association. Converters and handlers must not independently interpret a v1 enum as a v3 bitmask.

`object_list` contains neither values nor complete schemas for unknown classes. A descriptor from another version is not substituted for an unknown version. After registering a definition, you can explicitly call `ObjectCatalog.Rebind`.

Date and time formats are preserved without changing tags. Clock.time uses octet-string in COSEM and SPODES 5, and date-time in SPODES 4. SPODES 5 uses date-time for Clock.daylight_savings_begin and Clock.daylight_savings_end. Original special fields remain available as bytes; conversion to DateTimeOffset requires a concrete date and deviation.

Extended/Demand Register uses a 12-octet octet-string in the Blue Book and date-time (tag 25) in SPODES. The SPODES register tables print tag 27 alongside date-time. The data types section defines date-time as tag 25 and four-octet time as tag 27. The SPODES profiles use tag 25 for the declared date-time type.

The local Extended Register captures value and time through `CaptureAsync`; reset updates the time while preserving status. Demand Register accepts samples through `RecordSampleAsync`. The default calculator returns a Float64 mean with equal weight for each sample, regardless of the interval between samples. It does not integrate energy or reconstruct values from delta tags: use `IDemandValueCalculator` for those calculations, time weighting, and rounding. The application supplies values in consistent scaler/unit units. The retention window is `period × number_of_periods`; samples at the lower boundary expire. On `next_period`, the current result becomes the last result; samples are cleared for a single period and retained for a sliding window. Changing period/number_of_periods or writing an average clears samples while preserving other attributes. Sample history lives in the handler and is not restored from storage after a restart. The application schedules periodic collection and next_period calls.

Register reset requires an initial numeric value to determine the zero type, or an injected default-value function. Unpopulated attributes remain absent; known standard defaults are `Demand Register.number_of_periods = 1` and empty Register Activation lists. Handlers serialize local measurement input with dispatcher operations. A batch of writes assumes that `IObjectStateStore.Set` succeeds; an arbitrary external store must provide its own transactions.

Register Activation checks that assigned objects of classes 3/4/5 exist, mask names are unique, and indices fall between 1 and the length of register_assignment. This implementation supports up to 255 assignments. Repeating add_mask with the same byte name replaces the mask; add_register appends an assignment while preserving positions. Deleting the selected mask returns TemporaryFailure: select another mask first. An empty byte name is valid. Unassigned registers are enabled; assigned registers without a selected mask are disabled. The application uses `IsEnabledAsync` when collecting measurements; the handler does not block arbitrary register state writes. If an assigned object is removed directly from the catalog, the application must reconcile the configuration afterward.

A codec input buffer may contain multiple messages; `Consumed` indicates the end of the first message. Errors and NeedMoreData return zero consumption. HDLC retains the final flag because it may open the next frame. LLC receives an already complete information message.

Profile Generic stores row values explicitly. Compact representation is preserved during loading without reordering or truncation; capture or changing the order may convert the buffer to a regular array. Selector 2 entry/column indices start at 1; an end index of 0 means through the end. Failed operations must not append a partial capture row.

## Client session

`EMCL.Client` depends on Core, Protocol, Objects, and Transport. It does not depend on HDLC or Wrapper. ACSE construction/validation policies and the APDU codec can be replaced explicitly; `IDlmsClientSession` allows replacing the entire client.

The session is single-use and does not establish a transport connection. Invalid correlation, a duplicate response, a disconnect, an unsupported message, exceeded PDU/GET limits, or cancellation after a send attempt terminates the association. Meter errors are preserved as remote operation errors and allow further exchanges. GET and SET blocks start at 1; order and the full invoke octet are checked separately for each request. SET needs a final block response with a result; an early block result or normal SET error aborts the transfer. No SET or block is retried. Missing or invalid confirmation after any SET send attempt raises DlmsWriteUnconfirmedException with the original cause; even a timeout or caller cancellation cannot establish whether the write took effect. Add `BlockTransferWithAction` or `GeneralBlockTransfer` to the proposed conformance to use those services. A missing or invalid response after an ACTION send attempt raises `DlmsActionUnconfirmedException`.

If the peer closes after delivering the final GET block, the association faults but
the completed response can finish local decoding and catalog export. Caller
cancellation and the exchange deadline still apply; a transfer without a final
block fails. Console progress and frame traces share synchronized output writers
so concurrent writes preserve complete log lines.

Release stops accepting new operations and drains existing ones. Dispose cancels operations, waits for reads/writes, and disposes the channel according to LeaveOpen. Custom channels must honor the cancellation contract for shutdown to complete correctly. An operation deadline covers all of its GET or SET blocks; the release deadline includes waiting for in-flight operations.

## Verification

After a Release build, run: `dotnet test --project tests/EMCL.Tests/EMCL.Tests.csproj -c Release --no-build --no-restore`.

xUnit v3 tests verify reference bytes for A-XDR, BER/ACSE, LN APDUs, and Wrapper; the independent CRC-16/X-25 check value `123456789 → 0x906E`; SNRM `7E A0 07 03 21 93 0F 01 7E`; every incomplete message prefix; bounded buffers; malformed input; exact class versions; v1/v3 rights; import; local behavior; sorting; extensions from another assembly; and equivalence of three APDU transmission paths. Reference vectors and sort orders use parameterized tests; transport resource ownership under repeated disposal is also checked.

Verification uses reference messages, local object pipelines and test peers. Physical meter results and DLMS UA certification remain unverified.

For classes 4/5/6, tests cover exact-version import and definition replacement, timestamp types, status preservation, reset/next_period, sliding windows and sample expiration, numerical stability of the mean, custom calculators, limits, cancellation, time moving backward, atomic validation failures, mask references/indices, binary GET/ACTION, and capture into Profile Generic.

Client tests exercise the full association, GET/SET/ACTION, catalog loading, and release lifecycle through a direct APDU channel and Wrapper with fragmented bytes. They check reversed concurrent responses, independent GET blocks and reference GET-next messages, rejected AARE and invalid parameters, separate PDU limits and zero-as-unlimited, LLS, notifications and overflow, meter errors and ACTION return-data, SET PDU boundaries and length determinants, negotiated SET blocks, early/final SET errors, corrupt acknowledgements, lost final SET confirmation, transfer resource bounds, controlled timeouts, cancellation during queue waiting/after sending/during association, Dispose with pending requests, and LeaveOpen. Client tests also inject a custom codec and ACSE strategy.

## Sources

The implementation was checked against the supplied local documents:

- Blue Book Edition 16 Part 2, Version 1.0, October 30, 2023: 4.1 (model and types), 4.3.1–4.3.5 (Data and registers), 4.3.6 (Profile Generic), 4.4.4 (Association LN), and 4.5.1 (Clock).
- STO 34.01-5.1-006-2023, version 4 (SPODES): 6.4 (addressing), 7.2 (data), 7.3.1–7.3.7, and the Association LN description.
- Preliminary STO 34.01-5.1-006-2025, version 5 (SPODES), supplied draft. The information model and Push behavior use sections 7 and 9. Appendix M determines the Kuznyechik suite and HLS identifiers.
- Blue Book Edition 17 Part 2, supplied excerpt, supplements the SPODES definitions where available. The full Edition 16 supplies referenced descriptions outside the excerpt.
- Green Book Edition 11, Version 1.0, December 21, 2021: sections 7–8 (Wrapper, HDLC/LLC), section 9 and ACSE/xDLMS ASN.1 definitions; 9.1.4.7–9.1.4.8 (conformance and PDU), 9.3.2–9.3.3 (association and release), the GET and SET block transfer descriptions (including 9.4.6.4 and Figure 124), and the SET ASN.1/XSD definitions.

The PDFs themselves are not copied into the repository.

The console example is tested through a real TCP loopback connection and Wrapper: reading object_list across multiple GET blocks, v1/v3 rights, saving JSON, and disconnecting after the final block. A separate HDLC/TCP test checks SNRM/UA, I-frame exchange, segment assembly, and RR. Other xUnit tests cover configuration, password sources, absence of passwords in exports, unknown classes/members and null for unreported rights, protection flags/selectors, cancellation, and preservation of existing files.

Serial tests cover line settings, configuration and export, binary byte transfer, idempotent disposal, I/O cancellation with a stream that ignores CancellationToken, cleanup errors for a disconnected device, open timeouts, and port disposal after a late open completion. These tests require no physical device.
