using System.Collections.Concurrent;
using EMCL.Core.Objects;
using EMCL.Core.Operations;
using EMCL.Core.Values;
using EMCL.Objects.Definitions;
using EMCL.Objects.Handlers;
using EMCL.Objects.Internal;

namespace EMCL.Objects.Clock;

/// <summary>Executes Clock operations through an injected clock source, including partial time writes and preset adjustments.</summary>
/// <param name="clock">The clock source that reads and adjusts local device time.</param>
/// <param name="measuringPeriod">The period used for clock rounding; null selects fifteen minutes.</param>
public sealed class ClockHandler(IClockSource clock, TimeSpan? measuringPeriod = null) : StoredValueHandler
{
    private const int QuarterHourMinutes = 15;
    private const int MaximumShiftSeconds = 900;

    private readonly TimeSpan period = measuringPeriod ?? TimeSpan.FromMinutes(QuarterHourMinutes);
    private readonly ConcurrentDictionary<LogicalName, (DateTimeOffset Time, DateTimeOffset Start, DateTimeOffset End)> presets = new();
    private static byte Status(CosemObject obj) => obj.State.TryGet(ClockMembers.Attributes.Status, out var status) ? checked((byte)status.AsUInt64()) : (byte)0;
    private static DlmsValue TimeValue(CosemObject obj, CosemDateTime time) => new(obj.Descriptor.Attributes.First(a => a.Index == ClockMembers.Attributes.Time).Type!.Value, time.Octets);
    /// <inheritdoc />
    public override ValueTask<OperationResult> ReadAsync(CosemObject cosemObject, CosemOperation operation, OperationContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return operation.Index == ClockMembers.Attributes.Time ? ValueTask.FromResult(OperationResult.Ok(TimeValue(cosemObject, CosemDateTime.FromDateTimeOffset(clock.GetTime(cosemObject.LogicalName), Status(cosemObject))))) : base.ReadAsync(cosemObject, operation, context, cancellationToken);
    }

    /// <inheritdoc />
    public override ValueTask<OperationResult> WriteAsync(CosemObject cosemObject, CosemOperation operation, OperationContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (operation.Index == ClockMembers.Attributes.Time)
        {
            var incoming = CosemDateTime.FromValue(operation.Value!).Octets;
            var merged = CosemDateTime.FromDateTimeOffset(clock.GetTime(cosemObject.LogicalName), Status(cosemObject)).Octets.ToArray();
            if (incoming[CosemDateTimeFields.YearHigh] != CosemDateTimeFields.Unspecified || incoming[CosemDateTimeFields.YearLow] != CosemDateTimeFields.Unspecified)
            {
                merged[CosemDateTimeFields.YearHigh] = incoming[CosemDateTimeFields.YearHigh];
                merged[CosemDateTimeFields.YearLow] = incoming[CosemDateTimeFields.YearLow];
            }

            for (int i = CosemDateTimeFields.Month; i <= CosemDateTimeFields.Hundredths; i++)
            {
                if (incoming[i] != CosemDateTimeFields.Unspecified)
                {
                    merged[i] = incoming[i];
                }
            }

            if (CosemDateTime.FromValue(operation.Value!).Deviation != CosemDateTimeFields.UnspecifiedDeviation)
            {
                merged[CosemDateTimeFields.DeviationHigh] = incoming[CosemDateTimeFields.DeviationHigh];
                merged[CosemDateTimeFields.DeviationLow] = incoming[CosemDateTimeFields.DeviationLow];
            }

            if (incoming[CosemDateTimeFields.Status] != CosemDateTimeFields.Unspecified)
            {
                merged[CosemDateTimeFields.Status] = incoming[CosemDateTimeFields.Status];
            }

            clock.SetTime(cosemObject.LogicalName, new CosemDateTime(merged).ToDateTimeOffset());
            cosemObject.State.Set(ClockMembers.Attributes.Status, DlmsValue.Number(DlmsType.UInt8, merged[CosemDateTimeFields.Status]));
        }

        return base.WriteAsync(cosemObject, operation, context, cancellationToken);
    }

    /// <inheritdoc />
    public override ValueTask<OperationResult> InvokeAsync(CosemObject cosemObject, CosemOperation operation, OperationContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var now = clock.GetTime(cosemObject.LogicalName);
        DateTimeOffset updated;
        if (operation.Index is >= ClockMembers.Methods.AdjustToQuarter and <= ClockMembers.Methods.AdjustToPresetTime && operation.Value?.AsInt64() != 0)
        {
            return ValueTask.FromResult(new OperationResult(CosemResultCode.TypeUnmatched));
        }

        switch (operation.Index)
        {
            case ClockMembers.Methods.AdjustToQuarter:
                updated = Round(now, TimeSpan.FromMinutes(QuarterHourMinutes));
                break;
            case ClockMembers.Methods.AdjustToMeasuringPeriod:
                updated = Round(now, period);
                break;
            case ClockMembers.Methods.AdjustToMinute:
                updated = Round(now, TimeSpan.FromMinutes(1));
                break;
            case ClockMembers.Methods.AdjustToPresetTime:
                if (!presets.TryGetValue(cosemObject.LogicalName, out var preset) || now < preset.Start || now > preset.End)
                {
                    return ValueTask.FromResult(new OperationResult(CosemResultCode.TemporaryFailure));
                }

                updated = preset.Time;
                break;
            case ClockMembers.Methods.PresetAdjustingTime:
                var fields = Schema.Structure(operation.Value!, ClockStructures.Preset.Count);
                var p = (CosemDateTime.FromValue(fields[ClockStructures.Preset.Time]).ToDateTimeOffset(), CosemDateTime.FromValue(fields[ClockStructures.Preset.Start]).ToDateTimeOffset(), CosemDateTime.FromValue(fields[ClockStructures.Preset.End]).ToDateTimeOffset());
                if (p.Item2 > p.Item3)
                {
                    throw new ArgumentException("Invalid preset interval.");
                }

                presets[cosemObject.LogicalName] = p;
                return ValueTask.FromResult(OperationResult.Ok());
            case ClockMembers.Methods.ShiftTime:
                long seconds = operation.Value!.AsInt64();
                if (seconds is < -MaximumShiftSeconds or > MaximumShiftSeconds)
                {
                    return ValueTask.FromResult(new OperationResult(CosemResultCode.ScopeOfAccessViolated));
                }

                clock.ShiftTime(cosemObject.LogicalName, TimeSpan.FromSeconds(seconds));
                return ValueTask.FromResult(OperationResult.Ok());
            default:
                return base.InvokeAsync(cosemObject, operation, context, cancellationToken);
        }

        clock.SetTime(cosemObject.LogicalName, updated);
        return ValueTask.FromResult(OperationResult.Ok());
    }

    private static DateTimeOffset Round(DateTimeOffset value, TimeSpan quantum)
    {
        if (quantum.Ticks <= 0 || quantum > TimeSpan.FromDays(1))
        {
            throw new ArgumentOutOfRangeException(nameof(quantum));
        }

        long remainder = value.TimeOfDay.Ticks % quantum.Ticks;
        return value.AddTicks(remainder * 2 < quantum.Ticks ? -remainder : quantum.Ticks - remainder);
    }
}
