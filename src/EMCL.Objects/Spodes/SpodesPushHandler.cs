using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using EMCL.Core.Access;
using EMCL.Core.Objects;
using EMCL.Core.Operations;
using EMCL.Core.Pipeline;
using EMCL.Core.Values;
using EMCL.Objects.Handlers;
using EMCL.Protocol.Apdu;

namespace EMCL.Objects.Spodes;

internal sealed class SpodesPushHandler(ISpodesPushTransport? transport, OperationDelegate execute, TimeProvider time,
    IAccessPolicy access) : StoredValueHandler
{
    private const sbyte CaptureList = 2;
    private const sbyte Destination = 3;
    private const sbyte Windows = 4;
    private const sbyte RandomDelay = 5;
    private const sbyte Retries = 6;
    private const sbyte RetryDelay = 7;
    private const sbyte OperationMethod = 11;
    private const sbyte LastConfirmation = 13;
    private const sbyte PendingAttribute = 0;
    private const sbyte Trigger = 1;
    private const sbyte Reset = 2;
    private const uint InvokeMask = 0x00ffffff;
    private readonly ConditionalWeakTable<CosemObject, Attempt> attempts = new();
    private int nextId;

    public override async ValueTask<OperationResult> InvokeAsync(CosemObject obj, CosemOperation operation,
        OperationContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (operation.Index == Reset)
        {
            attempts.Remove(obj);
            obj.State.Set(PendingAttribute, DlmsValue.Null);
            return OperationResult.Ok();
        }

        if (transport is null)
        {
            return new(CosemResultCode.ObjectUnavailable);
        }

        if (operation.Index == Trigger)
        {
            if (!obj.State.TryGet(CaptureList, out var captures) || !obj.State.TryGet(Destination, out var destination))
            {
                return new(CosemResultCode.ObjectUnavailable);
            }

            var values = new List<DlmsValue>(captures.Items.Length);
            foreach (var capture in captures.Items)
            {
                var fields = capture.Items;
                ushort classId = (ushort)fields[0].AsUInt64();
                var name = LogicalName.FromBytes(fields[1].Bytes.AsSpan());
                sbyte attribute = (sbyte)fields[2].AsInt64();
                ushort index = (ushort)fields[3].AsUInt64();
                DlmsValue value;
                if (name == obj.LogicalName && classId == CosemClassIds.PushSetup && attribute == CaptureList)
                {
                    var read = new CosemOperation(CosemOperationKind.Read, classId, name, attribute);
                    if (!access.Allows(obj, read, context))
                    {
                        return new(CosemResultCode.ReadWriteDenied);
                    }

                    value = captures;
                }
                else
                {
                    if (fields[4].Items[0].AsUInt64() != 0 || !fields[5].Items.IsEmpty)
                    {
                        return new(CosemResultCode.ScopeOfAccessViolated);
                    }

                    var result = await execute(new(CosemOperationKind.Read, classId, name, attribute), context, ct).ConfigureAwait(false);
                    if (result.Error != CosemResultCode.Success || result.Value is null)
                    {
                        return result;
                    }

                    value = result.Value;
                }

                if (index != 0)
                {
                    if (value.Type is not (DlmsType.Structure or DlmsType.Array) || index > value.Items.Length)
                    {
                        return new(CosemResultCode.ScopeOfAccessViolated);
                    }

                    value = value.Items[index - 1];
                }

                values.Add(value);
            }

            uint invoke = unchecked((uint)Interlocked.Increment(ref nextId)) & InvokeMask;
            if (!obj.State.TryGet(OperationMethod, out var mode) || mode.AsUInt64() == 2)
            {
                invoke |= DataNotification.ConfirmedService;
            }

            if (obj.Descriptor.Key.ClassVersion == 3 && obj.LogicalName.ToBytes()[1] is >= 10 and <= 19)
            {
                invoke |= DataNotification.SelfDescriptive;
            }

            var now = time.GetUtcNow();
            int randomDelay = obj.State.TryGet(RandomDelay, out var delay) ? (int)delay.AsUInt64() : 0;
            var first = new Attempt(new(invoke, CosemDateTime.FromDateTimeOffset(now).Octets,
                DlmsValue.Structure([.. values])), destination, 0, now.AddSeconds(RandomNumberGenerator.GetInt32(randomDelay + 1)));
            Save(obj, first);
        }

        return await TrySendAsync(obj, ct).ConfigureAwait(false);
    }

    internal async ValueTask<OperationResult> ProcessAsync(CosemObject obj, CancellationToken ct)
    {
        await obj.OperationLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return transport is null ? new(CosemResultCode.ObjectUnavailable) : await TrySendAsync(obj, ct).ConfigureAwait(false);
        }
        finally
        {
            obj.OperationLock.Release();
        }
    }

    private async ValueTask<OperationResult> TrySendAsync(CosemObject obj, CancellationToken ct)
    {
        if (!attempts.TryGetValue(obj, out var pending))
        {
            if (!obj.State.TryGet(PendingAttribute, out var snapshot) || snapshot.Type == DlmsType.Null)
            {
                return OperationResult.Ok();
            }

            var fields = snapshot.Items;
            pending = new(new((uint)fields[0].AsUInt64(), fields[1].Bytes, fields[2]), fields[3],
                (int)fields[4].AsInt64(), new(fields[5].AsInt64(), TimeSpan.Zero));
            attempts.Add(obj, pending);
        }

        var now = time.GetUtcNow();
        if (pending.Due > now || !WindowOpen(obj, now))
        {
            return OperationResult.Ok();
        }

        bool delivered;
        try
        {
            delivered = await transport!.SendAsync(obj.LogicalName, pending.Destination, pending.Message, ct).ConfigureAwait(false);
        }
        catch (IOException)
        {
            delivered = false;
        }

        if (delivered)
        {
            attempts.Remove(obj);
            obj.State.Set(PendingAttribute, DlmsValue.Null);
            if (pending.Message.IsConfirmed)
            {
                obj.State.Set(LastConfirmation, new(DlmsType.DateTime, CosemDateTime.FromDateTimeOffset(time.GetUtcNow()).Octets));
            }

            return OperationResult.Ok();
        }

        int retries = obj.State.TryGet(Retries, out var configured) ? (int)configured.AsUInt64() : 0;
        if (pending.Retry >= retries)
        {
            attempts.Remove(obj);
            obj.State.Set(PendingAttribute, DlmsValue.Null);
            return new(CosemResultCode.TemporaryFailure);
        }

        int retry = pending.Retry + 1;
        double seconds = 0;
        if (obj.State.TryGet(RetryDelay, out var delays))
        {
            var fields = delays.Items;
            double minimum = fields[0].AsUInt64();
            seconds = minimum == 0 ? 0 : Math.Min(fields[2].AsUInt64(), minimum * Math.Pow(fields[1].AsUInt64() * 0.01, retry - 1));
        }

        Save(obj, pending with
        {
            Retry = retry,
            Due = time.GetUtcNow().AddSeconds(seconds)
        });
        return new(CosemResultCode.TemporaryFailure);
    }

    private static bool WindowOpen(CosemObject obj, DateTimeOffset now)
    {
        if (!obj.State.TryGet(Windows, out var windows) || windows.Items.IsEmpty)
        {
            return true;
        }

        foreach (var window in windows.Items)
        {
            var values = window.Items;
            if (TryWindowBoundary(values[0], now, out var start) && TryWindowBoundary(values[1], now, out var end)
                && (end >= start ? now >= start && now <= end : now >= start || now <= end))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryWindowBoundary(DlmsValue value, DateTimeOffset now, out DateTimeOffset boundary)
    {
        var configured = value.Bytes;
        var bytes = CosemDateTime.FromDateTimeOffset(now).Octets.ToArray();
        for (int i = 0; i < 9; i++)
        {
            if (configured[i] != 255)
            {
                bytes[i] = configured[i];
            }
        }

        if (CosemDateTime.FromValue(value).Deviation != short.MinValue)
        {
            bytes[9] = configured[9];
            bytes[10] = configured[10];
        }

        try
        {
            boundary = new CosemDateTime(bytes).ToDateTimeOffset();
            return true;
        }
        catch (Exception error) when (error is InvalidOperationException or ArgumentException)
        {
            boundary = default;
            return false;
        }
    }

    private void Save(CosemObject obj, Attempt attempt)
    {
        attempts.Remove(obj);
        attempts.Add(obj, attempt);
        obj.State.Set(PendingAttribute, DlmsValue.Structure(
            DlmsValue.Number(DlmsType.UInt32, attempt.Message.LongInvokeIdAndPriority),
            new(DlmsType.OctetString, attempt.Message.DateTime), attempt.Message.Body, attempt.Destination,
            DlmsValue.Number(DlmsType.UInt16, attempt.Retry), DlmsValue.Number(DlmsType.Int64, attempt.Due.UtcTicks)));
    }

    private sealed record Attempt(DataNotification Message, DlmsValue Destination, int Retry, DateTimeOffset Due);
}
