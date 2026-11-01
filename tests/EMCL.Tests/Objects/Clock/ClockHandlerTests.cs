using EMCL.Composition;
using EMCL.Core.Objects;
using EMCL.Core.Operations;
using EMCL.Core.Values;
using EMCL.Objects.Clock;
using EMCL.Tests.Support;

namespace EMCL.Tests.Objects.Clock;

public sealed class ClockHandlerTests
{
    [Fact]
    public async Task AdjustsAndPresetsTheDeviceClock()
    {
        var source = new MemoryClockSource(new FixedTimeProvider(new(2026, 10, 26, 12, 34, 45, TimeSpan.Zero)));
        var b = new EmclBuilder
        {
            ClockFactory = () => source
        };
        var d = b.Build("d");
        var clock = d.Add(8, 0, LogicalName.Parse("0.0.1.0.0.255"));
        Assert.Equal(CosemError.Success, (await ExecuteOperationAsync(d, clock, CosemOperationKind.Invoke, 3, Number(DlmsType.Int8, 0))).Error);
        Assert.Equal(35, source.GetTime(clock.LogicalName).Minute);
        Assert.Equal(0, source.GetTime(clock.LogicalName).Second);
        await ExecuteOperationAsync(d, clock, CosemOperationKind.Invoke, 6, Number(DlmsType.Int16, -60));
        Assert.Equal(34, source.GetTime(clock.LogicalName).Minute);
        Assert.Equal(CosemError.ScopeOfAccessViolated, (await ExecuteOperationAsync(d, clock, CosemOperationKind.Invoke, 6, Number(DlmsType.Int16, 901))).Error);
        await WriteValueAsync(d, clock, 2, DlmsValue.Octets(FromHex("FFFFFFFFFF0102FFFF8000FF")));
        Assert.Equal(1, source.GetTime(clock.LogicalName).Hour);
        Assert.Equal(2, source.GetTime(clock.LogicalName).Minute);
        source.SetTime(clock.LogicalName, new(2026, 10, 26, 12, 7, 31, TimeSpan.Zero));
        Assert.Equal(CosemError.Success, (await ExecuteOperationAsync(d, clock, CosemOperationKind.Invoke, 1, Number(DlmsType.Int8, 0))).Error);
        Assert.Equal(15, source.GetTime(clock.LogicalName).Minute);
        source.SetTime(clock.LogicalName, new(2026, 10, 26, 12, 7, 31, TimeSpan.Zero));
        d.Handlers.ForClass(clock.Descriptor.Key, new ClockHandler(source, TimeSpan.FromMinutes(5)));
        Assert.Equal(CosemError.Success, (await ExecuteOperationAsync(d, clock, CosemOperationKind.Invoke, 2, Number(DlmsType.Int8, 0))).Error);
        Assert.Equal(10, source.GetTime(clock.LogicalName).Minute);
        DlmsValue Time(int hour) => DlmsValue.Octets(CosemDateTime.FromDateTimeOffset(new(2026, 10, 26, hour, 0, 0, TimeSpan.Zero)).Octets.AsSpan());
        Assert.Equal(CosemError.Success, (await ExecuteOperationAsync(d, clock, CosemOperationKind.Invoke, 5, DlmsValue.Structure(Time(15), Time(12), Time(13)))).Error);
        Assert.Equal(CosemError.Success, (await ExecuteOperationAsync(d, clock, CosemOperationKind.Invoke, 4, Number(DlmsType.Int8, 0))).Error);
        Assert.Equal(15, source.GetTime(clock.LogicalName).Hour);
        Assert.Equal(CosemError.TemporaryFailure, (await ExecuteOperationAsync(d, clock, CosemOperationKind.Invoke, 4, Number(DlmsType.Int8, 0))).Error);
    }
}
