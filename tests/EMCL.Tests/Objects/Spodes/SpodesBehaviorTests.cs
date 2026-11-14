using EMCL.Composition;
using EMCL.Core.Objects;
using EMCL.Core.Operations;
using EMCL.Core.Values;
using EMCL.Objects.Definitions;
using EMCL.Objects.Spodes;
using EMCL.Protocol.Apdu;

namespace EMCL.Tests.Objects.Spodes;

public sealed class SpodesBehaviorTests
{
    private static readonly LogicalName Name = LogicalName.Parse("0.0.128.0.0.255");

    [Fact]
    public async Task ScriptStopsOnFailureAndRejectsRecursion()
    {
        var device = new EmclBuilder(CosemProfile.Spodes5).Build("device");
        var script = device.Add(9, 0, Name);
        var data = device.Add(1, 0, LogicalName.Parse("0.0.128.0.1.255"));
        script.State.Set(2, DlmsValue.Array(DlmsValue.Structure(U16(1), DlmsValue.Array(
            ScriptAction(1, data, 2, U16(10)),
            ScriptAction(2, script, 1, U16(1)),
            ScriptAction(1, data, 2, U16(20))))));
        var result = await Call(device, script, CosemOperationKind.Invoke, 1, U16(1));
        Assert.Equal(CosemResultCode.ScopeOfAccessViolated, result.Error);
        Assert.Equal(10UL, (await Call(device, data, CosemOperationKind.Read, 2)).Value!.AsUInt64());
    }

    [Fact]
    public async Task ScheduledScriptAndMonitorRunThroughTheDevicePipeline()
    {
        var device = new EmclBuilder(CosemProfile.Spodes4).Build("device");
        var script = device.Add(9, 0, Name);
        var target = device.Add(1, 0, LogicalName.Parse("0.0.128.0.1.255"));
        var schedule = device.Add(22, 0, LogicalName.Parse("0.0.128.0.2.255"));
        var source = device.Add(3, 0, LogicalName.Parse("1.0.1.8.0.255"));
        var monitor = device.Add(21, 0, LogicalName.Parse("0.0.128.0.3.255"));
        script.State.Set(2, DlmsValue.Array(
            DlmsValue.Structure(U16(1), DlmsValue.Array(ScriptAction(1, target, 2, U16(1)))),
            DlmsValue.Structure(U16(2), DlmsValue.Array(ScriptAction(1, target, 2, U16(2))))));
        schedule.State.Set(2, Reference(script, 1));
        schedule.State.Set(4, DlmsValue.Array(DlmsValue.Structure(
            DlmsValue.Octets([11, 20, 0, 0]), DlmsValue.Octets([0xff, 0xff, 0xff, 0xff, 0xff]))));
        monitor.State.Set(2, DlmsValue.Array(U16(10)));
        monitor.State.Set(3, DlmsValue.Structure(U16(3), DlmsValue.Octets(source.LogicalName.ToBytes()), I8(2)));
        monitor.State.Set(4, DlmsValue.Array(DlmsValue.Structure(Reference(script, 2), Reference(script, 1))));
        var now = new DateTimeOffset(2026, 1, 2, 11, 20, 0, TimeSpan.Zero);
        var ct = TestContext.Current.CancellationToken;
        var automation = device.SpodesAutomation!;
        Assert.Equal(CosemResultCode.Success, Assert.Single(await automation.TickAsync(now, device.LocalContext, ct)).Error);
        Assert.Empty(await automation.TickAsync(now, device.LocalContext, ct));
        Assert.Equal(1UL, (await Call(device, target, CosemOperationKind.Read, 2)).Value!.AsUInt64());
        Assert.Empty(await automation.ObserveAsync(source.LogicalName, 2, U16(9), now, device.LocalContext, cancellationToken: ct));
        Assert.Equal(CosemResultCode.Success, Assert.Single(await automation.ObserveAsync(source.LogicalName, 2,
            U16(10), now.AddSeconds(1), device.LocalContext, cancellationToken: ct)).Error);
        Assert.Equal(2UL, (await Call(device, target, CosemOperationKind.Read, 2)).Value!.AsUInt64());
    }

    [Fact]
    public async Task CanResumeOutOfOrderImageBlocksAndActivateOnlyAfterVerification()
    {
        var image = new ImageTarget();
        var device = new EmclBuilder(CosemProfile.Spodes4) { Spodes = new() { Images = image } }.Build("device");
        var obj = device.Add(18, 0, Name);
        Assert.Equal(CosemResultCode.Success, (await Call(device, obj, CosemOperationKind.Write, 2, U32(4))).Error);
        var start = DlmsValue.Structure(DlmsValue.Octets([42]), U32(6));
        Assert.Equal(CosemResultCode.Success, (await Call(device, obj, CosemOperationKind.Invoke, 1, start)).Error);
        var last = DlmsValue.Structure(U32(1), DlmsValue.Octets([5, 6]));
        Assert.Equal(CosemResultCode.Success, (await Call(device, obj, CosemOperationKind.Invoke, 2, last)).Error);
        Assert.Equal(CosemResultCode.Success, (await Call(device, obj, CosemOperationKind.Invoke, 1, start)).Error);
        Assert.Equal(0UL, (await Call(device, obj, CosemOperationKind.Read, 4)).Value!.AsUInt64());
        Assert.Equal(CosemResultCode.TemporaryFailure, (await Call(device, obj, CosemOperationKind.Invoke, 3, I8(0))).Error);
        Assert.Equal(CosemResultCode.Success, (await Call(device, obj, CosemOperationKind.Invoke, 2,
            DlmsValue.Structure(U32(0), DlmsValue.Octets([1, 2, 3, 4])))).Error);
        Assert.Equal(CosemResultCode.Success, (await Call(device, obj, CosemOperationKind.Invoke, 3, I8(0))).Error);
        Assert.Equal(CosemResultCode.Success, (await Call(device, obj, CosemOperationKind.Invoke, 4, I8(0))).Error);
        Assert.Equal(new byte[] { 1, 2, 3, 4, 5, 6 }, image.Activated);
    }

    [Fact]
    public async Task DelayedRelayResumesCheckpointAndPreservesStateOnHardwareFailure()
    {
        var clock = new Clock();
        var adapter = new Relay();
        byte[] ldn = [1, 2, 3, 4, 5, 6, 7, 8];
        var builder = new EmclBuilder(CosemProfile.Spodes5)
        {
            MeasurementTimeProvider = clock,
            Spodes = new() { Device = adapter, ResolveDeviceName = _ => ldn }
        };
        var device = builder.Build("device");
        var relay = device.Add(70, 2, Name);
        relay.State.Set(2, new(DlmsType.Boolean, true));
        relay.State.Set(3, DlmsValue.Number(DlmsType.Enum, 1));
        relay.State.Set(4, DlmsValue.Number(DlmsType.Enum, 2));
        relay.State.Set(DisconnectControlMembers.DelayedMode, new(DlmsType.Boolean, true));
        var start = await Call(device, relay, CosemOperationKind.Invoke, DisconnectControlMembers.InitializeDelayed,
            DlmsValue.Structure(DlmsValue.Number(DlmsType.UInt8, 24), DlmsValue.Number(DlmsType.UInt8, 24), U16(168)));
        Assert.Equal(CosemResultCode.Success, start.Error);
        clock.Advance(TimeSpan.FromHours(12));
        Assert.Equal(129UL, (await Call(device, relay, CosemOperationKind.Read, 3)).Value!.AsUInt64());

        var restarted = builder.Build("device");
        restarted.Catalog.Add(relay);
        clock.Advance(TimeSpan.FromHours(1000));
        Assert.Equal(129UL, (await Call(restarted, relay, CosemOperationKind.Read, 3)).Value!.AsUInt64());
        clock.Advance(TimeSpan.FromHours(12));
        Assert.Equal(130UL, (await Call(restarted, relay, CosemOperationKind.Read, 3)).Value!.AsUInt64());
        var confirmation = DlmsValue.Structure(start.Value!, DlmsValue.Octets(ldn));
        Assert.Equal(CosemResultCode.HardwareFault, (await Call(restarted, relay, CosemOperationKind.Invoke,
            DisconnectControlMembers.ConfirmDelayed, confirmation)).Error);
        Assert.Equal(true, (await Call(restarted, relay, CosemOperationKind.Read, 2)).Value!.Value);
        adapter.Fails = false;
        Assert.Equal(CosemResultCode.Success, (await Call(restarted, relay, CosemOperationKind.Invoke,
            DisconnectControlMembers.ConfirmDelayed, confirmation)).Error);
        Assert.Equal(false, (await Call(restarted, relay, CosemOperationKind.Read, 2)).Value!.Value);
    }

    [Fact]
    public async Task PushKeepsPendingMessageUntilConfirmed()
    {
        var pushTransport = new PushTransport();
        var device = new EmclBuilder(CosemProfile.Spodes5) { Spodes = new() { Push = pushTransport } }.Build("device");
        var obj = device.Add(40, 3, LogicalName.Parse("0.0.25.9.0.255"));
        obj.State.Set(2, DlmsValue.Array(DlmsValue.Structure(U16(40), DlmsValue.Octets(obj.LogicalName.ToBytes()), I8(2), U16(0),
            DlmsValue.Structure(DlmsValue.Number(DlmsType.Enum, 0), DlmsValue.Null), DlmsValue.Array())));
        obj.State.Set(3, DlmsValue.Structure(DlmsValue.Number(DlmsType.Enum, 0), DlmsValue.Octets([]), DlmsValue.Number(DlmsType.Enum, 0)));
        obj.State.Set(6, DlmsValue.Number(DlmsType.UInt8, 1));
        Assert.Equal(CosemResultCode.TemporaryFailure, (await Call(device, obj, CosemOperationKind.Invoke, 1, I8(0))).Error);
        Assert.Equal(CosemResultCode.Success, Assert.Single(await device.SpodesAutomation!.TickAsync(DateTimeOffset.UtcNow,
            device.LocalContext, TestContext.Current.CancellationToken)).Error);
        Assert.Equal(2, pushTransport.Sent.Count);
        Assert.Equal(pushTransport.Sent[0].LongInvokeIdAndPriority, pushTransport.Sent[1].LongInvokeIdAndPriority);
        Assert.Single(SpodesPushMessage.Bind(pushTransport.Sent[1]));
        Assert.Equal(DlmsType.DateTime, (await Call(device, obj, CosemOperationKind.Read, 13)).Value!.Type);
    }

    private static DlmsValue I8(int value) => DlmsValue.Number(DlmsType.Int8, value);
    private static DlmsValue U16(int value) => DlmsValue.Number(DlmsType.UInt16, value);
    private static DlmsValue U32(int value) => DlmsValue.Number(DlmsType.UInt32, value);
    private static DlmsValue Reference(CosemObject script, int selector)
        => DlmsValue.Structure(DlmsValue.Octets(script.LogicalName.ToBytes()), U16(selector));
    private static DlmsValue ScriptAction(int service, CosemObject obj, sbyte index, DlmsValue parameter)
        => DlmsValue.Structure(DlmsValue.Number(DlmsType.Enum, service), U16(obj.Descriptor.Key.ClassId),
            DlmsValue.Octets(obj.LogicalName.ToBytes()), I8(index), parameter);
    private static ValueTask<OperationResult> Call(EmclDevice device, CosemObject obj, CosemOperationKind kind,
        sbyte index, DlmsValue? parameter = null)
        => device.Pipeline.ExecuteAsync(new(kind, obj.Descriptor.Key.ClassId, obj.LogicalName, index, parameter),
            device.LocalContext, TestContext.Current.CancellationToken);

    private sealed class ImageTarget : ISpodesImageTarget
    {
        internal byte[]? Activated
        {
            get; private set;
        }
        public ValueTask<ReadOnlyMemory<byte>> VerifyAsync(LogicalName logicalName, ReadOnlyMemory<byte> identifier,
            ReadOnlyMemory<byte> image, CancellationToken cancellationToken) => ValueTask.FromResult(ReadOnlyMemory<byte>.Empty);
        public ValueTask ActivateAsync(LogicalName logicalName, ReadOnlyMemory<byte> identifier,
            ReadOnlyMemory<byte> image, CancellationToken cancellationToken)
        {
            Activated = image.ToArray();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class Clock : TimeProvider
    {
        private long timestamp;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => timestamp;
        internal void Advance(TimeSpan time) => timestamp += time.Ticks;
    }

    private sealed class Relay : ISpodesDeviceAdapter
    {
        internal bool Fails { get; set; } = true;
        public ValueTask<OperationResult> ExecuteAsync(CosemObject cosemObject, CosemOperation operation,
            OperationContext context, CancellationToken cancellationToken)
            => ValueTask.FromResult(Fails ? new OperationResult(CosemResultCode.HardwareFault) : OperationResult.Ok());
    }

    private sealed class PushTransport : ISpodesPushTransport
    {
        internal List<DataNotification> Sent { get; } = [];
        public ValueTask<bool> SendAsync(LogicalName source, DlmsValue destination,
            DataNotification notification, CancellationToken cancellationToken)
        {
            Sent.Add(notification);
            return ValueTask.FromResult(Sent.Count == 2);
        }
    }
}
