using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using EMCL.Core.Objects;
using EMCL.Core.Operations;
using EMCL.Core.Values;
using EMCL.Objects.Definitions;
using EMCL.Objects.Handlers;

namespace EMCL.Objects.Spodes;

internal sealed class SpodesRelayHandler(SpodesLocalOptions options, TimeProvider time) : StoredValueHandler
{
    private const sbyte Output = 2;
    private const sbyte ControlState = 3;
    private const sbyte Mode = 4;
    private const sbyte RemoteDisconnect = 1;
    private const sbyte RemoteReconnect = 2;
    private const sbyte ManualDisconnect = 3;
    private const sbyte ManualReconnect = 4;
    private const sbyte Checkpoint = 0;
    private readonly ConditionalWeakTable<CosemObject, Delay> delays = new();

    public override async ValueTask<OperationResult> ReadAsync(CosemObject obj, CosemOperation operation,
        OperationContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        Advance(obj);
        if (operation.Index == DisconnectControlMembers.DelayedMode && !obj.State.TryGet(operation.Index, out _))
        {
            return OperationResult.Ok(new(DlmsType.Boolean, false));
        }

        return await base.ReadAsync(obj, operation, context, ct).ConfigureAwait(false);
    }

    public override ValueTask<OperationResult> WriteAsync(CosemObject obj, CosemOperation operation,
        OperationContext context, CancellationToken ct)
    {
        Advance(obj);
        if (operation.Index == DisconnectControlMembers.DelayedMode &&
            (!options.IsConfigurator(context) || IsDelayed(obj) && operation.Value!.Value is false && !options.IsOpticalPort(context)))
        {
            return ValueTask.FromResult(new OperationResult(CosemResultCode.ReadWriteDenied));
        }

        if (operation.Index != Mode && operation.Index != DisconnectControlMembers.DelayedMode)
        {
            return ValueTask.FromResult(new OperationResult(CosemResultCode.ReadWriteDenied));
        }

        return base.WriteAsync(obj, operation, context, ct);
    }

    public override async ValueTask<OperationResult> InvokeAsync(CosemObject obj, CosemOperation operation,
        OperationContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var result = await InvokeCoreAsync(obj, operation, context, ct).ConfigureAwait(false);
        if (options.RelayAudit is { } audit)
        {
            await audit(obj.LogicalName, operation, result, ct).ConfigureAwait(false);
        }

        return result;
    }

    private async ValueTask<OperationResult> InvokeCoreAsync(CosemObject obj, CosemOperation op,
        OperationContext ctx, CancellationToken ct)
    {
        Advance(obj);
        if (!obj.State.TryGet(Mode, out var modeValue) || !obj.State.TryGet(ControlState, out var stateValue))
        {
            return new(CosemResultCode.ObjectUnavailable);
        }

        int mode = (int)modeValue.AsInt64();
        var state = (RelayState)stateValue.AsInt64();
        bool delayed = IsDelayed(obj);
        if (mode == 0 || delayed && op.Index is RemoteDisconnect or ManualDisconnect
            || !delayed && op.Index < 0)
        {
            return new(CosemResultCode.ReadWriteDenied);
        }

        if (state == RelayState.Blocked)
        {
            return new(CosemResultCode.TemporaryFailure);
        }

        if (op.Index == DisconnectControlMembers.InitializeDelayed)
        {
            if (delays.TryGetValue(obj, out var active))
            {
                Block(obj, active);
                return new(CosemResultCode.TemporaryFailure);
            }

            if (state == RelayState.Disconnected)
            {
                return new(CosemResultCode.TemporaryFailure);
            }

            var fields = op.Value!.Items;
            ulong session = BinaryPrimitives.ReadUInt64BigEndian(RandomNumberGenerator.GetBytes(sizeof(ulong)));
            var delay = new Delay(session, state, RelayState.Waiting, TimeSpan.FromHours(fields[0].AsInt64()),
                TimeSpan.FromHours(fields[1].AsInt64()), TimeSpan.FromHours(fields[2].AsInt64()), time.GetTimestamp());
            Save(obj, delay);
            return OperationResult.Ok(new(DlmsType.UInt64, session));
        }

        if (op.Index == DisconnectControlMembers.BlockControl)
        {
            if (state is RelayState.Disconnected or RelayState.Ready)
            {
                return OperationResult.Ok();
            }

            var blocking = TimeSpan.FromHours(op.Value!.AsInt64());
            if (delays.TryGetValue(obj, out var active))
            {
                Save(obj, active with
                {
                    Phase = RelayState.Blocked,
                    Remaining = blocking,
                    Timestamp = time.GetTimestamp()
                });
            }
            else
            {
                Save(obj, new(0, state, RelayState.Blocked, blocking, TimeSpan.Zero, blocking, time.GetTimestamp()));
            }

            return OperationResult.Ok();
        }

        if (op.Index == DisconnectControlMembers.ConfirmDelayed)
        {
            if (!delays.TryGetValue(obj, out var active))
            {
                return new(CosemResultCode.TemporaryFailure);
            }

            var fields = op.Value!.Items;
            var ldn = options.ResolveDeviceName?.Invoke(obj);
            if (ldn is null)
            {
                return new(CosemResultCode.ObjectUnavailable);
            }

            if (active.Phase != RelayState.Confirming || fields[0].AsUInt64() != active.Session
                || !fields[1].Bytes.AsSpan().SequenceEqual(ldn.Value.Span))
            {
                Block(obj, active);
                return new(CosemResultCode.ReadWriteDenied);
            }

            return await ApplyAsync(obj, op, ctx, RelayState.Disconnected, ct).ConfigureAwait(false);
        }

        if (delays.TryGetValue(obj, out var inProgress))
        {
            if (op.Index != RemoteReconnect)
            {
                Block(obj, inProgress);
                return new(CosemResultCode.TemporaryFailure);
            }

            state = inProgress.Original;
        }

        var next = op.Index switch
        {
            RemoteDisconnect => RelayState.Disconnected,
            RemoteReconnect when state == RelayState.Connected => RelayState.Connected,
            RemoteReconnect when mode is 2 or 4 or 7 or 130 or 133 => RelayState.Connected,
            RemoteReconnect => RelayState.Ready,
            ManualDisconnect when mode is 1 or 2 or 5 => RelayState.Disconnected,
            ManualReconnect when state == RelayState.Ready => RelayState.Connected,
            _ => state
        };
        if (next == state && !delays.TryGetValue(obj, out _))
        {
            return OperationResult.Ok();
        }

        return await ApplyAsync(obj, op, ctx, next, ct).ConfigureAwait(false);
    }

    private async ValueTask<OperationResult> ApplyAsync(CosemObject obj, CosemOperation op,
        OperationContext ctx, RelayState next, CancellationToken ct)
    {
        if (options.Device is null)
        {
            return new(CosemResultCode.ObjectUnavailable);
        }

        var result = await options.Device.ExecuteAsync(obj, op, ctx, ct).ConfigureAwait(false);
        if (result.Error == CosemResultCode.Success)
        {
            obj.State.Set(Output, new(DlmsType.Boolean, next == RelayState.Connected));
            Complete(obj, next);
        }

        return result;
    }

    private void Advance(CosemObject obj)
    {
        if (!delays.TryGetValue(obj, out var delay))
        {
            if (!obj.State.TryGet(Checkpoint, out var checkpoint) || checkpoint.Type == DlmsType.Null)
            {
                return;
            }

            var fields = checkpoint.Items;
            delay = new(fields[0].AsUInt64(), (RelayState)fields[1].AsInt64(), (RelayState)fields[2].AsInt64(),
                TimeSpan.FromTicks(fields[3].AsInt64()), TimeSpan.FromTicks(fields[4].AsInt64()),
                TimeSpan.FromTicks(fields[5].AsInt64()), time.GetTimestamp());
            delays.Add(obj, delay);
        }

        long timestamp = time.GetTimestamp();
        var elapsed = time.GetElapsedTime(delay.Timestamp, timestamp);
        var remaining = delay.Remaining - elapsed;
        while (remaining <= TimeSpan.Zero)
        {
            switch (delay.Phase)
            {
                case RelayState.Waiting:
                    delay = delay with
                    {
                        Phase = RelayState.Confirming
                    };
                    remaining += delay.Confirmation;
                    break;
                case RelayState.Confirming:
                    delay = delay with
                    {
                        Phase = RelayState.Blocked
                    };
                    remaining += delay.Blocking;
                    break;
                default:
                    Complete(obj, delay.Original);
                    return;
            }
        }

        Save(obj, delay with
        {
            Remaining = remaining,
            Timestamp = timestamp
        });
    }

    internal async ValueTask CheckpointAsync(CosemObject obj, CancellationToken ct)
    {
        await obj.OperationLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            Advance(obj);
        }
        finally
        {
            obj.OperationLock.Release();
        }
    }

    private void Block(CosemObject obj, Delay delay) => Save(obj, delay with
    {
        Phase = RelayState.Blocked,
        Remaining = delay.Blocking,
        Timestamp = time.GetTimestamp()
    });

    private void Save(CosemObject obj, Delay delay)
    {
        delays.Remove(obj);
        delays.Add(obj, delay);
        obj.State.Set(ControlState, DlmsValue.Number(DlmsType.Enum, (int)delay.Phase));
        obj.State.Set(Checkpoint, DlmsValue.Structure(new(DlmsType.UInt64, delay.Session),
            DlmsValue.Number(DlmsType.Enum, (int)delay.Original), DlmsValue.Number(DlmsType.Enum, (int)delay.Phase),
            DlmsValue.Number(DlmsType.Int64, delay.Remaining.Ticks),
            DlmsValue.Number(DlmsType.Int64, delay.Confirmation.Ticks), DlmsValue.Number(DlmsType.Int64, delay.Blocking.Ticks)));
    }

    private void Complete(CosemObject obj, RelayState state)
    {
        delays.Remove(obj);
        obj.State.Set(Checkpoint, DlmsValue.Null);
        obj.State.Set(ControlState, DlmsValue.Number(DlmsType.Enum, (int)state));
    }

    private static bool IsDelayed(CosemObject obj) => obj.Descriptor.Key.ClassVersion == 2
        && obj.State.TryGet(DisconnectControlMembers.DelayedMode, out var value) && value.Value is true;

    private sealed record Delay(ulong Session, RelayState Original, RelayState Phase,
        TimeSpan Remaining, TimeSpan Confirmation, TimeSpan Blocking, long Timestamp);

    private enum RelayState
    {
        Disconnected, Connected, Ready, Waiting = 129, Confirming = 130, Blocked = 131
    }
}
