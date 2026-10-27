using EMCL.Core.Operations;

namespace EMCL.Core.Pipeline;

/// <summary>Executes an object operation within a logical-device context.</summary>
/// <param name="operation">The requested object operation.</param>
/// <param name="context">The logical-device, association, and protection context.</param>
/// <param name="cancellationToken">A token that requests cancellation of the asynchronous operation.</param>
public delegate ValueTask<OperationResult> OperationDelegate(CosemOperation operation, OperationContext context, CancellationToken cancellationToken);
