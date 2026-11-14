using EMCL.Core.Objects;
using EMCL.Core.Operations;
using EMCL.Core.Pipeline;
using EMCL.Core.Values;
using EMCL.Objects.Handlers;

namespace EMCL.Objects.Spodes;

internal sealed class SpodesArbitratorHandler(OperationDelegate execute) : StoredValueHandler
{
    private const sbyte Actions = 2;
    private const sbyte Permissions = 3;
    private const sbyte Weights = 4;
    private const sbyte RecentRequests = 5;
    private const sbyte LastOutcome = 6;
    private const sbyte Reset = 2;

    public override async ValueTask<OperationResult> InvokeAsync(CosemObject obj, CosemOperation operation,
        OperationContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!obj.State.TryGet(Actions, out var actions) || !obj.State.TryGet(Permissions, out var permissions)
            || !obj.State.TryGet(Weights, out var weights))
        {
            return new(CosemResultCode.ObjectUnavailable);
        }

        int count = actions.Items.Length;
        int actors = permissions.Items.Length;
        if (weights.Items.Length != actors || permissions.Items.Any(row => ((DlmsBitString)row.Value!).BitCount != count)
            || weights.Items.Any(row => row.Items.Length != count))
        {
            return new(CosemResultCode.TypeUnmatched);
        }

        var empty = new DlmsValue(DlmsType.BitString, new DlmsBitString(count, new byte[(count + 7) / 8]));
        var rows = Enumerable.Repeat(empty, actors).ToArray();
        if (operation.Index == Reset)
        {
            obj.State.Set(RecentRequests, DlmsValue.Array(rows));
            obj.State.Set(LastOutcome, DlmsValue.Number(DlmsType.UInt8, 0));
            return OperationResult.Ok();
        }

        if (obj.State.TryGet(RecentRequests, out var previous) && previous.Items.Length == actors
            && previous.Items.All(row => ((DlmsBitString)row.Value!).BitCount == count))
        {
            previous.Items.CopyTo(rows);
        }

        var fields = operation.Value!.Items;
        int actor = (int)fields[0].AsUInt64();
        var request = (DlmsBitString)fields[1].Value!;
        if (actor >= actors || request.BitCount != count)
        {
            return new(CosemResultCode.ScopeOfAccessViolated);
        }

        byte[] allowed = request.Bytes.ToArray();
        var mask = ((DlmsBitString)permissions.Items[actor].Value!).Bytes;
        for (int i = 0; i < allowed.Length; i++)
        {
            allowed[i] &= mask[i];
        }

        rows[actor] = new(DlmsType.BitString, new DlmsBitString(count, allowed));
        int winner = -1;
        ulong highest = 0;
        for (int i = 0; i < count; i++)
        {
            ulong weight = 0;
            for (int row = 0; row < rows.Length; row++)
            {
                if (HasBit((DlmsBitString)rows[row].Value!, i))
                {
                    weight += weights.Items[row].Items[i].AsUInt64();
                }
            }

            if (weight > highest)
            {
                highest = weight;
                winner = i;
            }
            else if (weight == highest)
            {
                winner = -1;
            }
        }

        obj.State.Set(RecentRequests, DlmsValue.Array(rows));
        if (winner >= 0)
        {
            var result = await SpodesScriptHandler.ExecuteReferenceAsync(actions.Items[winner], execute, context,
                cancellationToken).ConfigureAwait(false);
            if (result.Error != CosemResultCode.Success)
            {
                return result;
            }

            obj.State.Set(LastOutcome, DlmsValue.Number(DlmsType.UInt8, winner + 1));
        }

        return OperationResult.Ok();
    }

    internal static bool HasBit(DlmsBitString bits, int index)
        => index >= 0 && index < bits.BitCount && (bits.Bytes[index / 8] & (0x80 >> (index % 8))) != 0;
}
