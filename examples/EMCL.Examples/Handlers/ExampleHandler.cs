using EMCL.Core.Objects;
using EMCL.Core.Operations;
using EMCL.Core.Values;
using EMCL.Objects.Handlers;

namespace EMCL.Examples.Handlers;

sealed class ExampleHandler : StoredValueHandler
{
    public override ValueTask<OperationResult> ReadAsync(CosemObject cosemObject, CosemOperation operation, OperationContext context, CancellationToken cancellationToken) => operation.Index == 2 ? ValueTask.FromResult(OperationResult.Ok(DlmsValue.Number(DlmsType.UInt32, 42))) : base.ReadAsync(cosemObject, operation, context, cancellationToken);
}
