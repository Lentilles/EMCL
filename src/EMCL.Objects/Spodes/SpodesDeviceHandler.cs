using EMCL.Core.Objects;
using EMCL.Core.Operations;
using EMCL.Objects.Handlers;

namespace EMCL.Objects.Spodes;

internal sealed class SpodesDeviceHandler(ISpodesDeviceAdapter? adapter) : StoredValueHandler
{
    public override ValueTask<OperationResult> ReadAsync(CosemObject cosemObject, CosemOperation operation,
        OperationContext context, CancellationToken cancellationToken)
        => operation.Index == CosemObject.LogicalNameAttributeIndex || adapter is null
            ? base.ReadAsync(cosemObject, operation, context, cancellationToken)
            : ReadDeviceAsync(cosemObject, operation, context, cancellationToken);

    private async ValueTask<OperationResult> ReadDeviceAsync(CosemObject obj, CosemOperation op,
        OperationContext context, CancellationToken cancellationToken)
    {
        var result = await adapter!.ExecuteAsync(obj, op, context, cancellationToken).ConfigureAwait(false);
        if (result.Error == CosemResultCode.Success
            && obj.Descriptor.Attributes.FirstOrDefault(attribute => attribute.Index == op.Index) is { } definition
            && (result.Value is not { } value || definition.Type is { } type && value.Type != type
                || definition.Constraint?.Invoke(value) is not null))
        {
            return new(CosemResultCode.TypeUnmatched, Diagnostic: "The adapter returned an invalid attribute value.");
        }

        return result.Error == CosemResultCode.ObjectUnavailable
            ? await base.ReadAsync(obj, op, context, cancellationToken).ConfigureAwait(false) : result;
    }

    public override async ValueTask<OperationResult> WriteAsync(CosemObject cosemObject, CosemOperation operation,
        OperationContext context, CancellationToken cancellationToken)
    {
        if (adapter is null)
        {
            return new(CosemResultCode.ObjectUnavailable);
        }

        var result = await adapter.ExecuteAsync(cosemObject, operation, context, cancellationToken).ConfigureAwait(false);
        return result.Error == CosemResultCode.Success
            ? await base.WriteAsync(cosemObject, operation, context, cancellationToken).ConfigureAwait(false) : result;
    }

    public override ValueTask<OperationResult> InvokeAsync(CosemObject cosemObject, CosemOperation operation,
        OperationContext context, CancellationToken cancellationToken)
        => adapter is null ? ValueTask.FromResult(new OperationResult(CosemResultCode.ObjectUnavailable))
            : adapter.ExecuteAsync(cosemObject, operation, context, cancellationToken);
}
