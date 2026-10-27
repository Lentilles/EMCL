using EMCL.Core.Operations;

namespace EMCL.Core.Pipeline;

/// <summary>Intercepts an operation and optionally invokes the next operation-pipeline component.</summary>
public interface IDlmsMiddleware
{
    /// <summary>Intercepts an operation and may delegate to the next component.</summary>
    /// <param name="operation">The requested object operation.</param>
    /// <param name="context">The logical-device, association, and protection context.</param>
    /// <param name="next">The next component in the operation pipeline.</param>
    /// <param name="cancellationToken">A token that requests cancellation of the asynchronous operation.</param>
    /// <returns>The intercepted or delegated operation result.</returns>
    ValueTask<OperationResult> InvokeAsync(CosemOperation operation, OperationContext context, OperationDelegate next, CancellationToken cancellationToken);
}
