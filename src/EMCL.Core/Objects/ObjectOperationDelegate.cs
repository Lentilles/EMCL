using EMCL.Core.Operations;

namespace EMCL.Core.Objects;

/// <summary>Executes an operation against a resolved COSEM object.</summary>
/// <param name="obj">The resolved object instance.</param>
/// <param name="operation">The requested object operation.</param>
/// <param name="context">The logical-device, association, and protection context.</param>
/// <param name="cancellationToken">A token that requests cancellation of the asynchronous operation.</param>
public delegate ValueTask<OperationResult> ObjectOperationDelegate(CosemObject obj, CosemOperation operation, OperationContext context, CancellationToken cancellationToken);
