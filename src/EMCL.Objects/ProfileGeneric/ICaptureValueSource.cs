using EMCL.Core.Operations;

namespace EMCL.Objects.ProfileGeneric;

/// <summary>Reads values for Profile Generic capture without imposing a storage or transport implementation.</summary>
public interface ICaptureValueSource
{
    /// <summary>Reads the selected attribute or component to be captured in a profile row.</summary>
    /// <param name="capture">The attribute and optional component to read.</param>
    /// <param name="context">The logical-device, association, and protection context.</param>
    /// <param name="cancellationToken">A token that requests cancellation of the asynchronous operation.</param>
    /// <returns>The value to capture or a COSEM error when no valid value is available.</returns>
    ValueTask<OperationResult> ReadAsync(CaptureObject capture, OperationContext context, CancellationToken cancellationToken);
}
