using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using EMCL.Core.Codecs;
using EMCL.Core.Objects;
using EMCL.Core.Operations;
using EMCL.Core.Values;
using EMCL.Objects.Definitions;
using EMCL.Objects.Handlers;
using EMCL.Protocol.Axdr;

namespace EMCL.Objects.Registers;

/// <summary>Executes Demand Register v0 operations and accepts application-driven instantaneous samples.</summary>
/// <remarks>The application schedules period transitions. The default calculator gives samples equal weight; inject a calculator for energy integration or device-specific demand rules. Changing period settings or writing an average discards pending samples without changing other stored attributes.</remarks>
public sealed class DemandRegisterHandler : StoredValueHandler
{
    private const ushort SinglePeriod = 1;

    private readonly TimeProvider timeProvider;
    private readonly IDemandValueCalculator calculator;
    private readonly Func<CosemObject, sbyte, DlmsValue>? resetValue;
    private readonly ProtocolLimits limits;
    private readonly ConditionalWeakTable<CosemObject, SampleBuffer> buffers = new();

    /// <summary>Creates an independent local demand handler.</summary>
    /// <param name="timeProvider">The measurement clock; null selects UTC system time.</param>
    /// <param name="calculator">The demand calculation strategy; null selects an equally weighted arithmetic mean.</param>
    /// <param name="resetValue">An instance- and attribute-specific default; null selects typed zero for initialized numeric averages.</param>
    /// <param name="limits">Bounds for values and retained samples; null selects validated defaults.</param>
    public DemandRegisterHandler(TimeProvider? timeProvider = null, IDemandValueCalculator? calculator = null,
        Func<CosemObject, sbyte, DlmsValue>? resetValue = null, ProtocolLimits? limits = null)
    {
        this.timeProvider = timeProvider ?? TimeProvider.System;
        this.calculator = calculator ?? new ArithmeticMeanDemandCalculator();
        this.resetValue = resetValue;
        this.limits = ProtocolLimits.Resolve(limits);
    }

    /// <summary>Records a measurement and updates current demand using samples younger than period times number_of_periods.</summary>
    /// <remarks>This producer API bypasses association access checks and serializes against dispatched operations. Period must be configured. Missing number_of_periods uses its standard default of one. Samples exactly on the lower window boundary expire. Failures leave stored values and sample history unchanged.</remarks>
    /// <param name="cosemObject">A Demand Register v0 instance.</param>
    /// <param name="sample">A numeric instantaneous sample.</param>
    /// <param name="cancellationToken">A token requesting cancellation.</param>
    /// <returns>The recording result, including explicit configuration, capacity, or value errors.</returns>
    public async ValueTask<OperationResult> RecordSampleAsync(CosemObject cosemObject, DlmsValue sample,
        CancellationToken cancellationToken = default)
    {
        RegisterValues.RequireClass(cosemObject, CosemClassIds.DemandRegister);
        ArgumentNullException.ThrowIfNull(sample);
        await cosemObject.OperationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!RegisterValues.IsNumeric(sample.Type))
            {
                return new(CosemError.TypeUnmatched, Diagnostic: "A demand sample must be numeric.");
            }

            if (!cosemObject.State.TryGet(DemandRegisterMembers.Attributes.Period, out var period))
            {
                return new(CosemError.ObjectUnavailable, Diagnostic: "Configure the demand period before recording samples.");
            }

            var count = PeriodCount(cosemObject);
            var error = RegisterValues.Validate(cosemObject, DemandRegisterMembers.Attributes.Period, period, limits)
                ?? RegisterValues.Validate(cosemObject, DemandRegisterMembers.Attributes.NumberOfPeriods, count, limits);
            if (error != null)
            {
                return new(CosemError.TypeUnmatched, Diagnostic: error);
            }

            var now = timeProvider.GetUtcNow();
            var buffer = buffers.GetValue(cosemObject, _ => new SampleBuffer());
            if (buffer.LastTimestamp.HasValue && now < buffer.LastTimestamp.Value)
            {
                return new(CosemError.TemporaryFailure, Diagnostic: "The measurement clock moved backwards.");
            }

            double windowSeconds = (double)period.AsUInt64() * count.AsUInt64();
            var retained = buffer.Samples.Where(s => (now - s.Timestamp).TotalSeconds < windowSeconds).ToImmutableArray();
            if (retained.Length >= limits.MaxCollectionItems)
            {
                return new(CosemError.TemporaryFailure, Diagnostic: "The demand sample limit was reached.");
            }

            var candidate = retained.Add(new(now, sample));
            DlmsValue value;
            try
            {
                // Encoding checks the sample's wire range before a custom calculator sees it.
                _ = new AxdrCodec(limits).EncodeBytes(sample);
                value = calculator.Calculate(cosemObject, candidate);
                error = RegisterValues.Validate(cosemObject, DemandRegisterMembers.Attributes.CurrentAverageValue, value, limits);
            }
            catch (Exception exception) when (exception is ArgumentException or FormatException or InvalidOperationException
                or OverflowException or InvalidCastException or NotSupportedException)
            {
                return new(CosemError.TypeUnmatched, Diagnostic: exception.Message);
            }

            if (error != null)
            {
                return new(CosemError.TypeUnmatched, Diagnostic: error);
            }

            var start = RegisterValues.Timestamp(cosemObject, DemandRegisterMembers.Attributes.StartTimeCurrent, now);
            cancellationToken.ThrowIfCancellationRequested();
            cosemObject.State.Set(DemandRegisterMembers.Attributes.CurrentAverageValue, value);
            if (!cosemObject.State.TryGet(DemandRegisterMembers.Attributes.StartTimeCurrent, out _))
            {
                cosemObject.State.Set(DemandRegisterMembers.Attributes.StartTimeCurrent, start);
            }

            buffer.Samples = candidate;
            buffer.LastTimestamp = now;
            return OperationResult.Ok();
        }
        finally
        {
            cosemObject.OperationLock.Release();
        }
    }

    /// <inheritdoc />
    public override ValueTask<OperationResult> ReadAsync(CosemObject cosemObject, CosemOperation operation,
        OperationContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return operation.Index == DemandRegisterMembers.Attributes.NumberOfPeriods && operation.Selection == null
            ? ValueTask.FromResult(OperationResult.Ok(PeriodCount(cosemObject)))
            : base.ReadAsync(cosemObject, operation, context, cancellationToken);
    }

    /// <inheritdoc />
    public override async ValueTask<OperationResult> WriteAsync(CosemObject cosemObject, CosemOperation operation,
        OperationContext context, CancellationToken cancellationToken)
    {
        var result = await base.WriteAsync(cosemObject, operation, context, cancellationToken).ConfigureAwait(false);
        if (result.Error == CosemError.Success && operation.Index is DemandRegisterMembers.Attributes.CurrentAverageValue or DemandRegisterMembers.Attributes.LastAverageValue or DemandRegisterMembers.Attributes.Period or DemandRegisterMembers.Attributes.NumberOfPeriods)
        {
            buffers.Remove(cosemObject);
        }

        return result;
    }

    /// <inheritdoc />
    public override ValueTask<OperationResult> InvokeAsync(CosemObject cosemObject, CosemOperation operation,
        OperationContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (operation.Index is not (DemandRegisterMembers.Methods.Reset or DemandRegisterMembers.Methods.NextPeriod))
        {
            return base.InvokeAsync(cosemObject, operation, context, cancellationToken);
        }

        if (!RegisterValues.IsZeroParameter(operation.Value))
        {
            return ValueTask.FromResult(new OperationResult(CosemError.TypeUnmatched));
        }

        var current = resetValue?.Invoke(cosemObject, DemandRegisterMembers.Attributes.CurrentAverageValue) ?? RegisterValues.Default(cosemObject, DemandRegisterMembers.Attributes.CurrentAverageValue);
        DlmsValue? last = operation.Index == DemandRegisterMembers.Methods.Reset
            ? resetValue?.Invoke(cosemObject, DemandRegisterMembers.Attributes.LastAverageValue) ?? RegisterValues.Default(cosemObject, DemandRegisterMembers.Attributes.LastAverageValue)
            : cosemObject.State.TryGet(DemandRegisterMembers.Attributes.CurrentAverageValue, out var previous) ? previous : null;
        if (current == null || last == null)
        {
            return ValueTask.FromResult(new OperationResult(CosemError.ObjectUnavailable));
        }

        var count = PeriodCount(cosemObject);
        var error = RegisterValues.Validate(cosemObject, DemandRegisterMembers.Attributes.CurrentAverageValue, current, limits)
            ?? RegisterValues.Validate(cosemObject, DemandRegisterMembers.Attributes.LastAverageValue, last, limits)
            ?? RegisterValues.Validate(cosemObject, DemandRegisterMembers.Attributes.NumberOfPeriods, count, limits);
        if (error != null)
        {
            return ValueTask.FromResult(new OperationResult(CosemError.TypeUnmatched, Diagnostic: error));
        }

        var now = timeProvider.GetUtcNow();
        var capture = RegisterValues.Timestamp(cosemObject, DemandRegisterMembers.Attributes.CaptureTime, now);
        var start = RegisterValues.Timestamp(cosemObject, DemandRegisterMembers.Attributes.StartTimeCurrent, now);
        cancellationToken.ThrowIfCancellationRequested();
        cosemObject.State.Set(DemandRegisterMembers.Attributes.CurrentAverageValue, current);
        cosemObject.State.Set(DemandRegisterMembers.Attributes.LastAverageValue, last);
        cosemObject.State.Set(DemandRegisterMembers.Attributes.CaptureTime, capture);
        cosemObject.State.Set(DemandRegisterMembers.Attributes.StartTimeCurrent, start);
        if (operation.Index == DemandRegisterMembers.Methods.Reset || count.AsUInt64() == SinglePeriod)
        {
            buffers.Remove(cosemObject);
        }

        return ValueTask.FromResult(OperationResult.Ok());
    }

    private static DlmsValue PeriodCount(CosemObject cosemObject) =>
        cosemObject.State.TryGet(DemandRegisterMembers.Attributes.NumberOfPeriods, out var count) ? count : DlmsValue.Number(DlmsType.UInt16, SinglePeriod);

    private sealed class SampleBuffer
    {
        internal ImmutableArray<DemandSample> Samples { get; set; } = [];
        internal DateTimeOffset? LastTimestamp
        {
            get; set;
        }
    }
}
