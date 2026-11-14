using EMCL.Core.Objects;
using EMCL.Core.Operations;
using EMCL.Core.Pipeline;
using EMCL.Core.Values;
using EMCL.Objects.Handlers;

namespace EMCL.Objects.Spodes;

internal sealed class SpodesScriptHandler(OperationDelegate execute) : StoredValueHandler
{
    internal const sbyte Execute = 1;
    private const sbyte Scripts = 2;
    private const int WriteAttribute = 1;

    public override async ValueTask<OperationResult> InvokeAsync(CosemObject obj, CosemOperation operation,
        OperationContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ulong selector = operation.Value!.AsUInt64();
        if (selector == 0)
        {
            return OperationResult.Ok();
        }

        if (!obj.State.TryGet(Scripts, out var scripts))
        {
            return new(CosemResultCode.ObjectUnavailable);
        }

        var script = scripts.Items.FirstOrDefault(entry => entry.Items[0].AsUInt64() == selector);
        if (script is null)
        {
            return new(CosemResultCode.ObjectUndefined);
        }

        foreach (var action in script.Items[1].Items)
        {
            var fields = action.Items;
            var kind = fields[0].AsUInt64() == WriteAttribute ? CosemOperationKind.Write : CosemOperationKind.Invoke;
            var target = new CosemOperation(kind, checked((ushort)fields[1].AsUInt64()),
                LogicalName.FromBytes(fields[2].Bytes.AsSpan()), checked((sbyte)fields[3].AsInt64()), fields[4]);
            var result = await execute(target, context, cancellationToken).ConfigureAwait(false);
            if (result.Error != CosemResultCode.Success)
            {
                return result;
            }
        }

        return OperationResult.Ok();
    }

    internal static ValueTask<OperationResult> ExecuteReferenceAsync(DlmsValue reference, OperationDelegate execute,
        OperationContext context, CancellationToken cancellationToken)
    {
        var fields = reference.Items;
        if (fields[1].AsUInt64() == 0)
        {
            return ValueTask.FromResult(OperationResult.Ok());
        }

        return execute(new(CosemOperationKind.Invoke, CosemClassIds.ScriptTable,
            LogicalName.FromBytes(fields[0].Bytes.AsSpan()), Execute, fields[1]), context, cancellationToken);
    }
}
