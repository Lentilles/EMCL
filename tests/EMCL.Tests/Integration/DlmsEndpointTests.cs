using EMCL.Composition;
using EMCL.Core.Codecs;
using EMCL.Core.Objects;
using EMCL.Core.Values;
using EMCL.Framing;
using EMCL.Framing.Hdlc.Frames;
using EMCL.Framing.Wrapper;
using EMCL.Messaging;
using EMCL.Protocol.Apdu;
using EMCL.Tests.Support;
using EMCL.Transport.Channels;

namespace EMCL.Tests.Integration;

public sealed class DlmsEndpointTests
{
    [Fact]
    public async Task ProducesEquivalentResponsesAcrossTransports()
    {
        var d = new EmclBuilder().Build("d");
        var r = d.Add(3, 0, LogicalName.Parse("1.0.1.8.0.255"));
        r.State.Set(2, Number(DlmsType.UInt32, 12345));
        var codec = new ApduCodec();
        var wire = FromHex("C001C100030100010800FF0200");
        var direct = await DlmsEndpoint.ExecuteAsync((LnRequest)DecodeComplete(codec, wire), d.Pipeline, d.LocalContext, cancellationToken: TestContext.Current.CancellationToken);
        var wrapper = new WrapperCodec().EncodeBytes(new(16, 1, wire.ToImmutableArray()));
        var sender = new HdlcApduAdapter(HdlcAddress.Client(16), HdlcAddress.Server(1));
        var hdlc = new HdlcCodec().EncodeBytes(sender.Frame(wire).Single());
        var channels = new IApduChannel[]
        {
            new FramedApduChannel<WrapperFrame>(new ChunkTransport(wrapper, 3), new WrapperCodec(), new WrapperApduAdapter(1, 16)),
            new FramedApduChannel<HdlcFrame>(new ChunkTransport(hdlc, 2), new HdlcCodec(), new HdlcApduAdapter(HdlcAddress.Server(1), HdlcAddress.Client(16)))
        };
        foreach (var c in channels)
        {
            await using var e = new DlmsEndpoint(c);
            var response = await DlmsEndpoint.ExecuteAsync(
                (LnRequest)(await e.ReceiveAsync(cancellationToken: TestContext.Current.CancellationToken))!,
                d.Pipeline,
                d.LocalContext,
                cancellationToken: TestContext.Current.CancellationToken
            );
            AssertBytesEqual(codec.EncodeBytes(direct), codec.EncodeBytes(response));
        }

        AssertBytesEqual(FromHex("C401C1000600003039"), codec.EncodeBytes(direct));
    }
}
