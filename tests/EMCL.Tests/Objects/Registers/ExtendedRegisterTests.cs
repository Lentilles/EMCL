using EMCL.Composition;
using EMCL.Core.Codecs;
using EMCL.Core.Objects;
using EMCL.Core.Operations;
using EMCL.Core.Values;
using EMCL.Objects.Registers;
using EMCL.Tests.Support;

namespace EMCL.Tests.Objects.Registers;

public sealed class ExtendedRegisterTests
{
    [Theory]
    [InlineData(DlmsType.OctetString)]
    [InlineData(DlmsType.DateTime)]
    public async Task CaptureAndResetUseConfiguredTimestampAndPreserveStatus(DlmsType timestampType)
    {
        var clock = new ManualTimeProvider();
        var builder = new EmclBuilder { MeasurementTimeProvider = clock };
        builder.Definitions.Register(ExtendedRegisterClassDefinition.Create(timestampType));
        var device = builder.Build("meter");
        var register = device.Add(4, 0, LogicalName.Parse("1.0.1.8.0.255"));
        var handler = Assert.IsType<ExtendedRegisterHandler>(device.Handlers.Resolve("meter", register));
        var status = Number(DlmsType.UInt32, 7);
        Assert.Equal(CosemError.Success, (await handler.CaptureAsync(register, Number(DlmsType.UInt64, 123), status,
            TestContext.Current.CancellationToken)).Error);
        var captured = ReadStoredValue(register, 5);
        Assert.Equal(timestampType, captured.Type);
        Assert.Equal(clock.Now, CosemDateTime.FromValue(captured).ToDateTimeOffset());
        clock.Now = clock.Now.AddSeconds(10);
        Assert.Equal(CosemError.Success, (await ExecuteOperationAsync(device, register, CosemOperationKind.Invoke, 1,
            Number(DlmsType.Int8, 0))).Error);
        Assert.Equal(DlmsType.UInt64, ReadStoredValue(register, 2).Type);
        Assert.Equal(0UL, ReadStoredValue(register, 2).AsUInt64());
        Assert.Same(status, ReadStoredValue(register, 4));
        Assert.Equal(clock.Now, CosemDateTime.FromValue(ReadStoredValue(register, 5)).ToDateTimeOffset());
        Assert.Equal(CosemError.Success, (await handler.CaptureAsync(register, Number(DlmsType.Float64, 9),
            cancellationToken: TestContext.Current.CancellationToken)).Error);
        Assert.Same(status, ReadStoredValue(register, 4));
    }

    [Fact]
    public async Task FailedCaptureAndResetDoNotPartiallyChangeState()
    {
        var builder = new EmclBuilder();
        builder.Definitions.Register(ExtendedRegisterClassDefinition.Create(DlmsType.DateTime, DefinitionConstraints.ScalarRegisterValue));
        var device = builder.Build("meter");
        var register = device.Add(4, 0, LogicalName.Parse("1.0.1.8.0.255"));
        var handler = Assert.IsType<ExtendedRegisterHandler>(device.Handlers.Resolve("meter", register));
        Assert.Equal(CosemError.ObjectUnavailable, (await ExecuteOperationAsync(device, register, CosemOperationKind.Invoke,
            1, Number(DlmsType.Int8, 0))).Error);
        var value = Number(DlmsType.UInt32, 42);
        Assert.Equal(CosemError.Success, (await handler.CaptureAsync(register, value,
            cancellationToken: TestContext.Current.CancellationToken)).Error);
        var timestamp = ReadStoredValue(register, 5);
        Assert.Equal(CosemError.TypeUnmatched, (await handler.CaptureAsync(register, Number(DlmsType.UInt32, 99),
            Number(DlmsType.Int8, -1), TestContext.Current.CancellationToken)).Error);
        Assert.Equal(CosemError.TypeUnmatched, (await handler.CaptureAsync(register, DlmsValue.Structure(value),
            cancellationToken: TestContext.Current.CancellationToken)).Error);
        Assert.Equal(CosemError.TypeUnmatched, (await ExecuteOperationAsync(device, register, CosemOperationKind.Invoke,
            1, Number(DlmsType.Int8, 1))).Error);
        Assert.Same(value, ReadStoredValue(register, 2));
        Assert.Same(timestamp, ReadStoredValue(register, 5));
    }

    [Fact]
    public async Task SupportsInstanceDefaultAndHonorsProducerLimitsAndCancellation()
    {
        var device = new EmclBuilder().Build("meter");
        var register = device.Add(4, 0, LogicalName.Parse("1.0.1.8.0.255"));
        var handler = new ExtendedRegisterHandler(resetValue: _ => DlmsValue.Octets([1, 2]),
            limits: new ProtocolLimits { MaxMessageBytes = 5 });
        device.Handlers.ForInstance("meter", register.LogicalName, handler);
        Assert.Equal(CosemError.Success, (await ExecuteOperationAsync(device, register, CosemOperationKind.Invoke,
            1, Number(DlmsType.Int8, 0))).Error);
        AssertBytesEqual([1, 2], ReadStoredValue(register, 2).Bytes.AsSpan());
        Assert.Equal(CosemError.TypeUnmatched, (await handler.CaptureAsync(register, DlmsValue.Octets([1, 2, 3, 4]),
            cancellationToken: TestContext.Current.CancellationToken)).Error);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await handler.CaptureAsync(register,
            Number(DlmsType.UInt8, 1), cancellationToken: cancellation.Token));
        AssertBytesEqual([1, 2], ReadStoredValue(register, 2).Bytes.AsSpan());
    }
}
