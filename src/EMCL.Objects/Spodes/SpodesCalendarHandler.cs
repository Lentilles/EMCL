using EMCL.Core.Objects;
using EMCL.Core.Operations;
using EMCL.Core.Values;
using EMCL.Objects.Handlers;

namespace EMCL.Objects.Spodes;

internal sealed class SpodesCalendarHandler : StoredValueHandler
{
    internal const sbyte Activate = 1;
    internal const sbyte ActivationTime = 10;
    private const sbyte FirstActive = 2;
    private const sbyte FirstPassive = 6;
    private const int TableCount = 4;

    public override ValueTask<OperationResult> InvokeAsync(CosemObject obj, CosemOperation operation,
        OperationContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var values = new DlmsValue[TableCount];
        for (int i = 0; i < TableCount; i++)
        {
            if (!obj.State.TryGet((sbyte)(FirstPassive + i), out var value))
            {
                return ValueTask.FromResult(new OperationResult(CosemResultCode.ObjectUnavailable));
            }

            values[i] = value;
        }

        var weekNames = values[2].Items.Select(week => Convert.ToHexString(week.Items[0].Bytes.AsSpan())).ToHashSet(StringComparer.Ordinal);
        var dayIds = values[3].Items.Select(day => day.Items[0].AsUInt64()).ToHashSet();
        if (weekNames.Count != values[2].Items.Length || dayIds.Count != values[3].Items.Length
            || values[1].Items.Any(season => !weekNames.Contains(Convert.ToHexString(season.Items[2].Bytes.AsSpan())))
            || values[2].Items.Any(week => week.Items.Skip(1).Any(day => !dayIds.Contains(day.AsUInt64()))))
        {
            return ValueTask.FromResult(new OperationResult(CosemResultCode.TypeUnmatched));
        }

        for (int i = 0; i < TableCount; i++)
        {
            obj.State.Set((sbyte)(FirstActive + i), values[i]);
        }

        return ValueTask.FromResult(OperationResult.Ok());
    }
}
