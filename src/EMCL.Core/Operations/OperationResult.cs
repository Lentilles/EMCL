using EMCL.Core.Values;

namespace EMCL.Core.Operations;

/// <summary>Contains an operation result code, optional data, and an optional local diagnostic.</summary>
/// <param name="Error">The operation or decode error, if any.</param>
/// <param name="Value">The returned attribute or method value, or null when no return data exists.</param>
/// <param name="Diagnostic">A local diagnostic that is not automatically transmitted on the wire.</param>
public sealed record OperationResult(CosemError Error, DlmsValue? Value = null, string? Diagnostic = null)
{
    /// <summary>Creates a successful operation result with optional return data.</summary>
    /// <param name="value">The value to convert, validate, store, or encode.</param>
    public static OperationResult Ok(DlmsValue? value = null) => new(CosemError.Success, value);
}
