using EMCL.Core.Objects;
using EMCL.Core.Operations;

namespace EMCL.Objects.Handlers;

/// <summary>Reads and writes stored attributes and rejects methods without an implementation.</summary>
public class StoredValueHandler : ICosemObjectHandler
{
    /// <inheritdoc />
    public virtual ValueTask<OperationResult> ReadAsync(CosemObject cosemObject, CosemOperation operation, OperationContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (operation.Selection != null)
        {
            return ValueTask.FromResult(new OperationResult(CosemError.ScopeOfAccessViolated));
        }

        return ValueTask.FromResult(cosemObject.State.TryGet(operation.Index, out var value) ? OperationResult.Ok(value) : new OperationResult(CosemError.ObjectUnavailable));
    }

    /// <inheritdoc />
    public virtual ValueTask<OperationResult> WriteAsync(CosemObject cosemObject, CosemOperation operation, OperationContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (operation.Index == CosemObject.LogicalNameAttributeIndex)
        {
            return ValueTask.FromResult(new OperationResult(CosemError.ReadWriteDenied));
        }

        if (operation.Selection != null)
        {
            return ValueTask.FromResult(new OperationResult(CosemError.ScopeOfAccessViolated));
        }

        cosemObject.State.Set(operation.Index, operation.Value ?? throw new ArgumentException("Missing value."));
        return ValueTask.FromResult(OperationResult.Ok());
    }

    /// <inheritdoc />
    public virtual ValueTask<OperationResult> InvokeAsync(CosemObject cosemObject, CosemOperation operation, OperationContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(new OperationResult(CosemError.ReadWriteDenied, Diagnostic: "Method is not implemented."));
    }
}
