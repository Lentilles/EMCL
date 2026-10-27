using EMCL.Core.Values;

namespace EMCL.Objects.Registers;

/// <summary>Represents an instantaneous measurement supplied to a local demand calculation.</summary>
/// <param name="Timestamp">The measurement instant assigned by the handler's time provider.</param>
/// <param name="Value">The numeric measurement before demand calculation.</param>
public sealed record DemandSample(DateTimeOffset Timestamp, DlmsValue Value);
