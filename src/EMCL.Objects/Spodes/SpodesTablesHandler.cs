using EMCL.Core.Objects;
using EMCL.Core.Operations;
using EMCL.Core.Values;
using EMCL.Objects.Handlers;

namespace EMCL.Objects.Spodes;

internal sealed class SpodesTablesHandler(int limit) : StoredValueHandler
{
    private const sbyte Entries = 2;

    public override ValueTask<OperationResult> ReadAsync(CosemObject cosemObject, CosemOperation operation,
        OperationContext context, CancellationToken cancellationToken)
        => operation.Index == Entries && !cosemObject.State.TryGet(Entries, out _)
            ? ValueTask.FromResult(OperationResult.Ok(DlmsValue.Array()))
            : base.ReadAsync(cosemObject, operation, context, cancellationToken);

    public override ValueTask<OperationResult> InvokeAsync(CosemObject cosemObject, CosemOperation operation,
        OperationContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var entries = cosemObject.State.TryGet(Entries, out var stored) ? stored.Items.ToList() : [];
        var parameter = operation.Value!;
        switch (cosemObject.Descriptor.Key.ClassId)
        {
            case CosemClassIds.Schedule:
                Schedule(entries, operation.Index, parameter);
                break;
            case CosemClassIds.SpecialDaysTable:
                if (operation.Index == 1)
                {
                    entries.RemoveAll(entry => entry.Items[1].Bytes.AsSpan().SequenceEqual(parameter.Items[1].Bytes.AsSpan()));
                    Replace(entries, parameter);
                }
                else
                {
                    entries.RemoveAll(entry => entry.Items[0].AsUInt64() == parameter.AsUInt64());
                }

                break;
            case CosemClassIds.SapAssignment:
                entries.RemoveAll(entry => entry.Items[1].Type == parameter.Items[1].Type && EqualName(entry.Items[1], parameter.Items[1]));
                if (parameter.Items[0].AsUInt64() != 0)
                {
                    Replace(entries, parameter);
                }

                break;
        }

        if (entries.Count > limit)
        {
            return ValueTask.FromResult(new OperationResult(CosemResultCode.TemporaryFailure));
        }

        cosemObject.State.Set(Entries, DlmsValue.Array([.. entries.OrderBy(entry => entry.Items[0].AsUInt64())]));
        return ValueTask.FromResult(OperationResult.Ok());
    }

    private static void Schedule(List<DlmsValue> entries, sbyte method, DlmsValue parameter)
    {
        var fields = parameter.Items;
        switch (method)
        {
            case 1:
                for (int i = 0; i < entries.Count; i++)
                {
                    var entry = entries[i];
                    ulong index = entry.Items[0].AsUInt64();
                    bool enable = InRange(index, fields[2], fields[3]);
                    if (enable || InRange(index, fields[0], fields[1]))
                    {
                        entries[i] = DlmsValue.Structure([.. entry.Items.SetItem(1, new(DlmsType.Boolean, enable))]);
                    }
                }

                break;
            case 2:
                Replace(entries, parameter);
                break;
            case 3:
                entries.RemoveAll(entry => InRange(entry.Items[0].AsUInt64(), fields[0], fields[1]));
                break;
        }
    }

    private static bool InRange(ulong index, DlmsValue first, DlmsValue last)
        => index >= first.AsUInt64() && index <= last.AsUInt64();

    private static void Replace(List<DlmsValue> entries, DlmsValue parameter)
    {
        entries.RemoveAll(entry => entry.Items[0].AsUInt64() == parameter.Items[0].AsUInt64());
        entries.Add(parameter);
    }

    private static bool EqualName(DlmsValue left, DlmsValue right)
        => left.Type == DlmsType.OctetString ? left.Bytes.AsSpan().SequenceEqual(right.Bytes.AsSpan()) : Equals(left.Value, right.Value);
}
