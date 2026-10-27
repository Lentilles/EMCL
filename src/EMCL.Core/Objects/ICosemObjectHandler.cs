using EMCL.Core.Operations;

namespace EMCL.Core.Objects;

/// <summary>Executes local object operations without depending on transport framing or session management.</summary>
public interface ICosemObjectHandler
{
    /// <summary>Reads an attribute or selected part of an attribute from the resolved object.</summary>
    /// <param name="cosemObject">The resolved object instance.</param>
    /// <param name="operation">The requested object operation.</param>
    /// <param name="context">The logical-device, association, and protection context.</param>
    /// <param name="cancellationToken">A token that requests cancellation of the asynchronous operation.</param>
    /// <returns>The attribute value or a COSEM error when it cannot be read.</returns>
    ValueTask<OperationResult> ReadAsync(CosemObject cosemObject, CosemOperation operation, OperationContext context, CancellationToken cancellationToken);
    /// <summary>Writes an attribute value to the resolved object.</summary>
    /// <param name="cosemObject">The resolved object instance.</param>
    /// <param name="operation">The requested object operation.</param>
    /// <param name="context">The logical-device, association, and protection context.</param>
    /// <param name="cancellationToken">A token that requests cancellation of the asynchronous operation.</param>
    /// <returns>The write result or a COSEM error without fabricating an attribute value.</returns>
    ValueTask<OperationResult> WriteAsync(CosemObject cosemObject, CosemOperation operation, OperationContext context, CancellationToken cancellationToken);
    /// <summary>Invokes a declared method on the resolved object.</summary>
    /// <param name="cosemObject">The resolved object instance.</param>
    /// <param name="operation">The requested object operation.</param>
    /// <param name="context">The logical-device, association, and protection context.</param>
    /// <param name="cancellationToken">A token that requests cancellation of the asynchronous operation.</param>
    /// <returns>The method result and optional return data.</returns>
    ValueTask<OperationResult> InvokeAsync(CosemObject cosemObject, CosemOperation operation, OperationContext context, CancellationToken cancellationToken);
}
