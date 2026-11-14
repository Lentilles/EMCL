using EMCL.Core.Objects;
using EMCL.Core.Operations;

namespace EMCL.Objects.Spodes;

/// <summary>Connects SPODES objects to the application that owns the meter, relay or communication hardware.</summary>
/// <remarks>Writes return success after the external change is applied. Reads return ObjectUnavailable when a value cannot be supplied.
/// An adapter must not reenter the operation pipeline for the object on which it is being called.</remarks>
public interface ISpodesDeviceAdapter
{
    /// <summary>Executes an operation for a configured device object.</summary>
    /// <param name="cosemObject">The object and its current attribute state.</param>
    /// <param name="operation">The validated operation.</param>
    /// <param name="context">The trusted local association and access context.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    ValueTask<OperationResult> ExecuteAsync(CosemObject cosemObject, CosemOperation operation,
        OperationContext context, CancellationToken cancellationToken);
}
