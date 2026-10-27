# Contributing to EMCL

## Library boundaries

- Keep application codecs independent of byte transports and frame formats. HDLC segmentation, LLC, Wrapper framing, and xDLMS GET blocks belong to separate layers.
- Resolve interface classes by the complete profile, class ID, and class-version key. Preserve unknown versions rather than silently substituting a known version.
- Keep association permissions separate from object state. Protection flags in an operation context must originate from a trusted security layer.
- Prefer composition through the public interfaces. Keep implementation helpers internal; use inheritance only for deliberate extension points such as `StoredValueHandler`.
- Give each public type its own file. Use descriptive parameter and local-variable names, explicit control-flow blocks, and small methods with a single responsibility.

## Source layout

Keep library projects under `src/`, test projects under `tests/`, runnable examples under `examples/`, and supplementary documentation under `docs/`. The solution uses the same top-level groups.

Within a project, group related contracts and implementations by responsibility: for example, values and descriptors in Core, ACSE and A-XDR codecs in Protocol, and association, clock, and profile behavior in Objects. Place implementation-only wire helpers in `Internal` and shared test fixtures in `Support`. Small projects with only a few closely related types can remain flat.

Mirror the relevant subsystem in test directories. Every namespace must match the project root namespace followed by the relative directory path: for example, `src/EMCL.Core/Access/AttributeAccess.cs` belongs to `EMCL.Core.Access`. Update imports, qualified references, XML documentation, and examples together when moving a type. SDK-style projects discover nested source files automatically. The `IDE0130` analyzer enforces this rule during builds using the root namespace and project directory exposed by `Directory.Build.props`.

## Public API documentation

Write XML documentation in English for public types, constructors, members, enum values, delegates, and interface contracts. Document positional record parameters as well: the compiler carries their descriptions into generated property documentation.

Describe semantics, not just names. Include input ownership, stream lifetime, absent-value behavior, version requirements, resource limits, and concurrency restrictions when relevant. Document parameters, return values, and significant exceptions. Use `<inheritdoc />` only when the inherited interface or base member provides the correct contract.

`Directory.Build.props` enables XML documentation output for every library. Missing public documentation is a build error; do not suppress `CS1591`. Documentation XML accompanies each library DLL and is included when packing the library.

## C# conventions

- Follow `.editorconfig`, nullable-reference annotations, and the enabled .NET analyzers. Keep warnings as errors.
- Validate required reference arguments at API boundaries with `ArgumentNullException.ThrowIfNull`; use the framework argument-validation helpers for scalar bounds.
- Preserve original DLMS tags and special date/time fields. Conversion into CLR values must be explicit and use invariant culture when formatting or parsing protocol identifiers.
- Do not use unexplained numeric protocol identifiers, field offsets, bitmasks, class IDs, versions, attribute/method indices, selectors, or access modes in implementation logic. Use descriptive enums or constants owned by the relevant protocol or object layer. Reuse `ApduTag`, `CosemClassIds`, and the class-specific `*Members` definitions. Keep literals in their named definitions and in independent binary test vectors; ordinary loop counters and basic arithmetic do not need artificial constants.
- Pass cancellation tokens through asynchronous operations. Use `ConfigureAwait(false)` inside asynchronous library implementations; let xUnit manage the synchronization context in tests.
- State resource ownership explicitly. Disposal must be idempotent, and disposed transports or endpoints must reject further I/O. Dispose only after active operations finish.
- Do not catch exceptions merely to ignore them. Distinguish malformed input, incomplete input, unsupported features, and COSEM operation errors.
- Avoid new runtime dependencies unless they solve a concrete problem that cannot reasonably be handled by the framework or existing abstractions.

## Tests and verification

Use xUnit v3 with Microsoft Testing Platform, as configured in `global.json`. Use the [official xUnit guide](https://xunit.net/docs/getting-started/v3/getting-started) for runner integration.

Write independent tests with behavior-based names. Prefer `[Theory]` for wire vectors and input/output tables. Use xUnit assertions directly; shared helpers should construct domain data or expose a reusable protocol assertion rather than implement another assertion framework. Pass `TestContext.Current.CancellationToken` to cancellable APIs.

For codecs, use independent expected bytes as well as round-trip checks. Test incomplete prefixes, malformed lengths, limits, and multi-message buffers. Keep public-extension and assembly-dependency tests in the separate test assembly.

Run from the repository root:

```sh
dotnet restore EMCL.slnx --disable-parallel
dotnet build EMCL.slnx -c Release --no-restore -m:1
dotnet test --project tests/EMCL.Tests/EMCL.Tests.csproj -c Release --no-build --no-restore
dotnet format whitespace EMCL.slnx --no-restore --verify-no-changes
```

Update the capability matrix when behavior or supported protocol variants change. Keep examples compilable and demonstrate extension through public contracts.
