using EMCL.Core.Objects;
using EMCL.Core.Operations;
using EMCL.Core.Values;
using EMCL.Objects.Definitions;
using EMCL.Objects.ProfileGeneric;

namespace EMCL.Objects.Handlers;

/// <summary>Executes Register reset using a custom reset function or a type-preserving numeric zero.</summary>
/// <param name="resetValue">An optional reset-value function; null resets initialized numeric registers to zero.</param>
public sealed class RegisterHandler(Func<CosemObject, DlmsValue>? resetValue = null) : StoredValueHandler
{
    /// <inheritdoc />
    public override ValueTask<OperationResult> InvokeAsync(CosemObject cosemObject, CosemOperation operation, OperationContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (operation.Index != RegisterMembers.Methods.Reset)
        {
            return base.InvokeAsync(cosemObject, operation, context, cancellationToken);
        }

        if (operation.Value?.Type != DlmsType.Int8 || operation.Value.AsInt64() != 0)
        {
            return ValueTask.FromResult(new OperationResult(CosemResultCode.TypeUnmatched));
        }

        DlmsValue value;
        if (resetValue != null)
        {
            value = resetValue(cosemObject);
        }
        else if (cosemObject.State.TryGet(RegisterMembers.Attributes.Value, out var current) && ValueComparison.IsNumeric(current.Type))
        {
            value = new(current.Type, current.Type is DlmsType.Float32 or DlmsType.Float64 ? 0d : 0L);
        }
        else
        {
            return ValueTask.FromResult(new OperationResult(CosemResultCode.ObjectUnavailable, Diagnostic: "Provide a reset value for a nonnumeric or uninitialized register."));
        }

        cosemObject.State.Set(RegisterMembers.Attributes.Value, value);
        return ValueTask.FromResult(OperationResult.Ok());
    }
}
