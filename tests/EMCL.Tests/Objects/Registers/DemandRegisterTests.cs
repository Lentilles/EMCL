using EMCL.Composition;
using EMCL.Core.Codecs;
using EMCL.Core.Objects;
using EMCL.Core.Operations;
using EMCL.Core.Values;
using EMCL.Objects.Registers;
using EMCL.Tests.Support;

namespace EMCL.Tests.Objects.Registers;

public sealed class DemandRegisterTests
{
    [Theory]
    [InlineData(DlmsType.OctetString, 1, 50d)]
    [InlineData(DlmsType.DateTime, 2, 30d)]
    public async Task TransitionsPeriodsAndRetainsOnlySlidingWindowSamples(DlmsType timestampType,
        int periods, double expected)
    {
        var clock = new ManualTimeProvider();
        var builder = new EmclBuilder { MeasurementTimeProvider = clock };
        builder.Definitions.Register(DemandRegisterClassDefinition.Create(timestampType));
        var device = builder.Build("meter");
        var register = device.Add(5, 0, LogicalName.Parse("1.0.1.6.0.255"));
        var handler = Assert.IsType<DemandRegisterHandler>(device.Handlers.Resolve("meter", register));
        await WriteValueAsync(device, register, 8, Number(DlmsType.UInt32, 10));
        await WriteValueAsync(device, register, 9, Number(DlmsType.UInt16, periods));
        await Sample(10);
        clock.Now = clock.Now.AddSeconds(5);
        await Sample(30);
        Assert.Equal(20d, ReadStoredValue(register, 2).Value);
        Assert.False(register.State.TryGet(6, out _));
        var status = Number(DlmsType.UInt16, 3);
        register.State.Set(5, status);
        clock.Now = clock.Now.AddSeconds(5);
        Assert.Equal(CosemResultCode.Success, (await ExecuteOperationAsync(device, register, CosemOperationKind.Invoke,
            2, Number(DlmsType.Int8, 0))).Error);
        Assert.Equal(20d, ReadStoredValue(register, 3).Value);
        Assert.Equal(0d, ReadStoredValue(register, 2).Value);
        Assert.Equal(timestampType, ReadStoredValue(register, 6).Type);
        Assert.Equal(clock.Now, CosemDateTime.FromValue(ReadStoredValue(register, 6)).ToDateTimeOffset());
        Assert.Equal(clock.Now, CosemDateTime.FromValue(ReadStoredValue(register, 7)).ToDateTimeOffset());
        await Sample(50);
        Assert.Equal(expected, ReadStoredValue(register, 2).Value);
        clock.Now = clock.Now.AddSeconds(periods * 10);
        await Sample(80);
        Assert.Equal(80d, ReadStoredValue(register, 2).Value);
        Assert.Equal(CosemResultCode.Success, (await ExecuteOperationAsync(device, register, CosemOperationKind.Invoke,
            1, Number(DlmsType.Int8, 0))).Error);
        Assert.Equal(0d, ReadStoredValue(register, 2).Value);
        Assert.Equal(0d, ReadStoredValue(register, 3).Value);
        Assert.Same(status, ReadStoredValue(register, 5));
        await Sample(90);
        Assert.Equal(90d, ReadStoredValue(register, 2).Value);

        async Task Sample(long value) => Assert.Equal(CosemResultCode.Success,
            (await handler.RecordSampleAsync(register, Number(DlmsType.Int32, value), TestContext.Current.CancellationToken)).Error);
    }

    [Fact]
    public async Task RejectsUnconfiguredPeriodInvalidMetadataAndOutOfOrderSamples()
    {
        var clock = new ManualTimeProvider();
        var device = new EmclBuilder { MeasurementTimeProvider = clock }.Build("meter");
        var register = device.Add(5, 0, LogicalName.Parse("1.0.1.6.0.255"));
        var handler = Assert.IsType<DemandRegisterHandler>(device.Handlers.Resolve("meter", register));
        Assert.Equal(CosemResultCode.ObjectUnavailable, (await Record(1)).Error);
        Assert.Equal(1UL, (await ExecuteOperationAsync(device, register, CosemOperationKind.Read, 9)).Value!.AsUInt64());
        foreach (sbyte index in new sbyte[] { 8, 9 })
        {
            Assert.Equal(CosemResultCode.TypeUnmatched, (await ExecuteOperationAsync(device, register, CosemOperationKind.Write,
                index, Number(index == 8 ? DlmsType.UInt32 : DlmsType.UInt16, 0))).Error);
        }

        await WriteValueAsync(device, register, 8, Number(DlmsType.UInt32, 60));
        Assert.Equal(CosemResultCode.Success, (await Record(10)).Error);
        var value = ReadStoredValue(register, 2);
        var start = ReadStoredValue(register, 7);
        clock.Now = clock.Now.AddSeconds(-1);
        Assert.Equal(CosemResultCode.TemporaryFailure, (await Record(100)).Error);
        clock.Now = clock.Now.AddSeconds(2);
        Assert.Equal(CosemResultCode.TypeUnmatched, (await handler.RecordSampleAsync(register, new(DlmsType.Float64, double.NaN),
            TestContext.Current.CancellationToken)).Error);
        Assert.Same(value, ReadStoredValue(register, 2));
        Assert.Same(start, ReadStoredValue(register, 7));
        Assert.Equal(CosemResultCode.Success, (await Record(30)).Error);
        Assert.Equal(20d, ReadStoredValue(register, 2).Value);

        ValueTask<OperationResult> Record(long value) => handler.RecordSampleAsync(register, Number(DlmsType.Int32, value),
            TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task BoundsHistoryAndDiscardsSamplesAfterConfigurationWrites()
    {
        var device = new EmclBuilder { Limits = new ProtocolLimits { MaxCollectionItems = 2 } }.Build("meter");
        var register = device.Add(5, 0, LogicalName.Parse("1.0.1.6.0.255"));
        var handler = Assert.IsType<DemandRegisterHandler>(device.Handlers.Resolve("meter", register));
        await WriteValueAsync(device, register, 8, Number(DlmsType.UInt32, 600));
        await Record(10);
        await Record(20);
        Assert.Equal(CosemResultCode.TemporaryFailure, (await Record(30)).Error);
        Assert.Equal(15d, ReadStoredValue(register, 2).Value);
        await WriteValueAsync(device, register, 9, Number(DlmsType.UInt16, 2));
        Assert.Equal(CosemResultCode.Success, (await Record(90)).Error);
        Assert.Equal(90d, ReadStoredValue(register, 2).Value);

        ValueTask<OperationResult> Record(long value) => handler.RecordSampleAsync(register, Number(DlmsType.Int16, value),
            TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task AllowsCustomCalculatorAndDefaultsAndRejectsInvalidCalculatedValueAtomically()
    {
        var calculator = new LastSampleCalculator();
        var builder = new EmclBuilder { DemandCalculatorFactory = () => calculator };
        builder.Definitions.Register(DemandRegisterClassDefinition.Create(DlmsType.DateTime, DefinitionConstraints.ScalarRegisterValue));
        var device = builder.Build("meter");
        var register = device.Add(5, 0, LogicalName.Parse("1.0.1.6.0.255"));
        var handler = Assert.IsType<DemandRegisterHandler>(device.Handlers.Resolve("meter", register));
        await WriteValueAsync(device, register, 8, Number(DlmsType.UInt32, 60));
        Assert.Equal(CosemResultCode.Success, (await Record(10)).Error);
        calculator.Invalid = true;
        Assert.Equal(CosemResultCode.TypeUnmatched, (await Record(99)).Error);
        Assert.Equal(10L, ReadStoredValue(register, 2).AsInt64());
        calculator.Invalid = false;
        Assert.Equal(CosemResultCode.Success, (await Record(30)).Error);
        Assert.Equal(2, calculator.LastCount);
        var custom = new DemandRegisterHandler(resetValue: (_, index) => Number(DlmsType.UInt32, index));
        device.Handlers.ForInstance("meter", register.LogicalName, custom);
        Assert.Equal(CosemResultCode.Success, (await ExecuteOperationAsync(device, register, CosemOperationKind.Invoke,
            1, Number(DlmsType.Int8, 0))).Error);
        Assert.Equal(2L, ReadStoredValue(register, 2).AsInt64());
        Assert.Equal(3L, ReadStoredValue(register, 3).AsInt64());

        ValueTask<OperationResult> Record(long value) => handler.RecordSampleAsync(register, Number(DlmsType.UInt32, value),
            TestContext.Current.CancellationToken);
    }

    [Theory]
    [InlineData(double.MaxValue, double.MaxValue, double.MaxValue)]
    [InlineData(double.MaxValue, -double.MaxValue, 0d)]
    public void MeanAvoidsIntermediateOverflow(double first, double second, double expected)
    {
        var register = new EmclBuilder().Build("meter").Add(5, 0, LogicalName.Parse("1.0.1.6.0.255"));
        var value = new ArithmeticMeanDemandCalculator().Calculate(register,
            [new(DateTimeOffset.UnixEpoch, new(DlmsType.Float64, first)), new(DateTimeOffset.UnixEpoch, new(DlmsType.Float64, second))]);
        Assert.Equal(expected, value.Value);
    }

    [Fact]
    public async Task IsolatesSampleHistoryAndPreservesStateWhenResetDefaultsAreUnavailable()
    {
        var device = new EmclBuilder().Build("meter");
        var first = device.Add(5, 0, LogicalName.Parse("1.0.1.6.0.255"));
        var second = device.Add(5, 0, LogicalName.Parse("1.0.2.6.0.255"));
        var handler = Assert.IsType<DemandRegisterHandler>(device.Handlers.Resolve("meter", first));
        foreach (var register in new[] { first, second })
        {
            await WriteValueAsync(device, register, 8, Number(DlmsType.UInt32, 60));
        }

        Assert.Equal(CosemResultCode.Success, (await Record(first, 10)).Error);
        Assert.Equal(CosemResultCode.Success, (await Record(second, 100)).Error);
        var value = ReadStoredValue(first, 2);
        var start = ReadStoredValue(first, 7);
        // last_average_value has no known type yet, so reset must not partially reset current_average_value.
        Assert.Equal(CosemResultCode.ObjectUnavailable, (await ExecuteOperationAsync(device, first, CosemOperationKind.Invoke,
            1, Number(DlmsType.Int8, 0))).Error);
        Assert.Same(value, ReadStoredValue(first, 2));
        Assert.Same(start, ReadStoredValue(first, 7));
        Assert.False(first.State.TryGet(6, out _));
        Assert.Equal(CosemResultCode.TypeUnmatched, (await handler.RecordSampleAsync(first, Number(DlmsType.UInt8, 999),
            TestContext.Current.CancellationToken)).Error);
        Assert.Equal(CosemResultCode.Success, (await Record(first, 30)).Error);
        Assert.Equal(20d, ReadStoredValue(first, 2).Value);
        Assert.Equal(100d, ReadStoredValue(second, 2).Value);

        ValueTask<OperationResult> Record(CosemObject register, long number) => handler.RecordSampleAsync(register,
            Number(DlmsType.UInt32, number), TestContext.Current.CancellationToken);
    }

    private sealed class LastSampleCalculator : IDemandValueCalculator
    {
        internal bool Invalid
        {
            get; set;
        }
        internal int LastCount
        {
            get; private set;
        }

        public DlmsValue Calculate(CosemObject cosemObject, IReadOnlyList<DemandSample> samples)
        {
            LastCount = samples.Count;
            return Invalid ? DlmsValue.Array() : samples[^1].Value;
        }
    }
}
