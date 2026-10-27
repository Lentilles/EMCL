using EMCL.Composition;
using EMCL.Core.Codecs;
using EMCL.Core.Descriptors;
using EMCL.Core.Objects;
using EMCL.Core.Operations;
using EMCL.Core.Values;
using EMCL.Messaging;
using EMCL.Profiles.BlueBook;
using EMCL.Profiles.Spodes;
using EMCL.Protocol.Apdu;
using EMCL.Protocol.Axdr;
using EMCL.Tests.Support;

namespace EMCL.Tests.Integration;

public sealed class ExtensibilityTests
{
    [Fact]
    public async Task HonorsUserOverridesAcrossPublicContracts()
    {
        var b = new EmclBuilder();
        var d = b.Build("d", BlueBook16Profile.Id);
        Assert.Equal(DlmsType.OctetString, b.Definitions.Find(new(BlueBook16Profile.Id, 8, 0))!.Attributes[1].Type!.Value);
        Assert.Equal(DlmsType.DateTime, b.Definitions.Find(new(Spodes4Profile.Id, 8, 0))!.Attributes[1].Type!.Value);
        Assert.True(b.Definitions.Find(new(BlueBook16Profile.Id, 15, 1)) == null && b.Definitions.Find(new(Spodes4Profile.Id, 15, 3)) == null);
        var o = d.Add(1, 0, LogicalName.Parse("0.0.96.1.0.255"));
        o.State.Set(2, Number(DlmsType.UInt8, 1));
        var op = new CosemOperation(CosemOperationKind.Read, 1, o.LogicalName, 2);
        Assert.Equal(1L, (await d.Pipeline.ExecuteAsync(op, d.LocalContext, cancellationToken: TestContext.Current.CancellationToken)).Value!.AsInt64());
        d.Handlers.ForClass(o.Descriptor.Key, new ConstantHandler(2));
        Assert.Equal(2L, (await d.Pipeline.ExecuteAsync(op, d.LocalContext, cancellationToken: TestContext.Current.CancellationToken)).Value!.AsInt64());
        d.Handlers.ForInstance("d", o.LogicalName, new ConstantHandler(3));
        Assert.Equal(3L, (await d.Pipeline.ExecuteAsync(op, d.LocalContext, cancellationToken: TestContext.Current.CancellationToken)).Value!.AsInt64());
        var custom = new CosemClassDescriptor(new(BlueBook16Profile.Id, 9000, 4), "Manufacturer", [new(2, "value")], [new(1, "method")]);
        b.Definitions.Register(custom);
        var m = d.Add(9000, 4, LogicalName.Parse("0.0.128.1.0.255"));
        d.Handlers.ForClass(custom.Key, new ConstantHandler(99));
        Assert.Equal(
            99L,
            (await d.Pipeline.ExecuteAsync(op with
            {
                ClassId = 9000,
                LogicalName = m.LogicalName
            }, d.LocalContext, cancellationToken: TestContext.Current.CancellationToken)).Value!.AsInt64()
        );
        var middleware = new CountingMiddleware();
        var mb = new EmclBuilder();
        mb.Middleware.Add(middleware);
        var md = mb.Build("m", BlueBook16Profile.Id);
        md.Add(1, 0, o.LogicalName);
        await md.Pipeline.ExecuteAsync(op, md.LocalContext, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(1, middleware.Count);
        var cb = new EmclBuilder
        {
            Converter = new PlusOneConverter(),
            Factory = new CustomFactory()
        };
        var cd = cb.Build("c", BlueBook16Profile.Id);
        Assert.True(cd.Add(1, 0, o.LogicalName).State is CustomStore);
        await cd.Pipeline.ExecuteAsync(op with
        {
            Kind = CosemOperationKind.Write,
            Value = Number(DlmsType.UInt8, 8)
        }, cd.LocalContext, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(9L, (await cd.Pipeline.ExecuteAsync(op, cd.LocalContext, cancellationToken: TestContext.Current.CancellationToken)).Value!.AsInt64());
        var vb = new EmclBuilder
        {
            Validator = new RejectValidator()
        };
        var vd = vb.Build("v", BlueBook16Profile.Id);
        vd.Add(1, 0, o.LogicalName);
        Assert.Equal(CosemError.TypeUnmatched, (await vd.Pipeline.ExecuteAsync(op, vd.LocalContext, cancellationToken: TestContext.Current.CancellationToken)).Error);
        d.Pipeline = new CustomPipeline();
        Assert.Equal(77L, (await d.Pipeline.ExecuteAsync(op, d.LocalContext, cancellationToken: TestContext.Current.CancellationToken)).Value!.AsInt64());
        await using var channel = new CustomChannel();
        await using var endpoint = new DlmsEndpoint(channel, new CustomCodec());
        var notification = (DataNotification)(await endpoint.ReceiveAsync(cancellationToken: TestContext.Current.CancellationToken))!;
        Assert.Equal(42L, notification.Body.AsInt64());
        await endpoint.SendAsync(notification, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(channel.Sent);
        var segment = new ByteSegment(new byte[] { 17 });
        var end = segment.Append(new byte[] { 1 });
        Assert.Equal(DecodeStatus.Done, new AxdrCodec().Decode(new(segment, 0, end, end.Memory.Length)).Status);
    }
}
