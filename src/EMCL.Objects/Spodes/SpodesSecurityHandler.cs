using System.Security.Cryptography;
using EMCL.Core.Objects;
using EMCL.Core.Operations;
using EMCL.Core.Values;
using EMCL.Objects.Handlers;

namespace EMCL.Objects.Spodes;

internal sealed class SpodesSecurityHandler(SpodesLocalOptions options) : StoredValueHandler
{
    private const sbyte Policy = 2;
    private const sbyte ActivatePolicy = 1;
    private const sbyte TransferKeys = 2;
    private const int ReservedPolicyBits = 3;
    private readonly SpodesDeviceHandler external = new(options.Device);

    public override ValueTask<OperationResult> WriteAsync(CosemObject obj, CosemOperation operation,
        OperationContext context, CancellationToken ct)
        => operation.Index == Policy ? ValueTask.FromResult(new OperationResult(CosemResultCode.ReadWriteDenied))
            : external.WriteAsync(obj, operation, context, ct);

    public override async ValueTask<OperationResult> InvokeAsync(CosemObject obj, CosemOperation operation,
        OperationContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (operation.Index is not (ActivatePolicy or TransferKeys))
        {
            return await external.InvokeAsync(obj, operation, context, ct).ConfigureAwait(false);
        }

        if (!options.IsConfigurator(context) || context.Association is not null && !context.AuthenticatedRequest)
        {
            return new(CosemResultCode.ReadWriteDenied);
        }

        if (options.AfterResponse is not { } afterResponse)
        {
            return new(CosemResultCode.ObjectUnavailable);
        }

        if (operation.Index == ActivatePolicy)
        {
            var requested = (byte)operation.Value!.AsUInt64();
            int existing = obj.State.TryGet(Policy, out var stored) ? (int)stored.AsUInt64() : 0;
            if ((requested & ReservedPolicyBits) != 0 || (requested & existing) != existing)
            {
                return new(CosemResultCode.ScopeOfAccessViolated);
            }

            afterResponse(obj.LogicalName, operation, token => ApplyPolicyAsync(obj, requested, token));
            return OperationResult.Ok();
        }

        if (options.KeyTransfer is null || options.SecurityStore is null)
        {
            return new(CosemResultCode.ObjectUnavailable);
        }

        var entries = operation.Value!.Items;
        var ids = entries.Select(item => (byte)item.Items[0].AsUInt64()).ToArray();
        if (ids.Length == 0 || ids.Distinct().Count() != ids.Length)
        {
            return new(CosemResultCode.TypeUnmatched);
        }

        var buffers = new List<byte[]>(ids.Length);
        try
        {
            var keys = new Dictionary<byte, ReadOnlyMemory<byte>>();
            for (int i = 0; i < ids.Length; i++)
            {
                var clear = options.KeyTransfer.UnwrapKey(entries[i].Items[1].Bytes.AsSpan());
                buffers.Add(clear);
                keys.Add(ids[i], clear);
            }

            var commit = await options.SecurityStore.StageAsync(obj.LogicalName, keys, ct).ConfigureAwait(false);
            afterResponse(obj.LogicalName, operation, commit);
            return OperationResult.Ok();
        }
        catch (CryptographicException)
        {
            return new(CosemResultCode.TypeUnmatched, Diagnostic: "The key batch could not be authenticated.");
        }
        finally
        {
            foreach (var buffer in buffers)
            {
                CryptographicOperations.ZeroMemory(buffer);
            }
        }
    }

    private static async ValueTask ApplyPolicyAsync(CosemObject obj, byte requested, CancellationToken ct)
    {
        await obj.OperationLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            ulong current = obj.State.TryGet(Policy, out var value) ? value.AsUInt64() : 0;
            obj.State.Set(Policy, DlmsValue.Number(DlmsType.UInt8, (long)(current | requested)));
        }
        finally
        {
            obj.OperationLock.Release();
        }
    }
}
