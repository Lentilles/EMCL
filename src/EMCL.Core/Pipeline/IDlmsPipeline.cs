using EMCL.Core.Operations;

namespace EMCL.Core.Pipeline;

/// <summary>Executes transport-independent object operations, including access checks and dispatch.</summary>
public interface IDlmsPipeline
{
    /// <summary>Executes an operation using the selected logical-device and association context.</summary>
    /// <param name="operation">The requested object operation.</param>
    /// <param name="context">The logical-device, association, and protection context.</param>
    /// <param name="cancellationToken">A token that requests cancellation of the asynchronous operation.</param>
    /// <returns>The operation result and optional return data.</returns>
    ValueTask<OperationResult> ExecuteAsync(CosemOperation operation, OperationContext context, CancellationToken cancellationToken = default);
}
