using EMCL.Client.Association;
using EMCL.Client.Exceptions;
using EMCL.Client.Sessions;
using EMCL.Core.Codecs;
using EMCL.Core.Objects;
using EMCL.Core.Values;
using EMCL.Protocol.Acse;
using EMCL.Protocol.Apdu;
using EMCL.Tests.Support;

namespace EMCL.Tests.Client;

public sealed class ClientNegotiationTests
{
    [Fact]
    public async Task EncodesTheConfirmedLnProposalAndValidatesNegotiatedParameters()
    {
        var pair = DuplexApduChannel.CreatePair();
        await using var peer = new SessionPeer(pair.Server);
        await using var client = new DlmsClientSession(pair.Client, SessionPeer.Options());
        var request = await peer.AssociateAsync(client);
        AssertBytesEqual(FromHex("0100010100065F1F040000109D0400"), request.UserInformation!.Value.AsSpan());
        AssertBytesEqual(FromHex("60857405080101"), request.ApplicationContext!.Value.AsSpan());
        await peer.ReleaseAsync(client);
    }

    [Theory]
    [InlineData(1, 0, 6, 7, 1024, 1, 0x109d)]
    [InlineData(0, 14, 6, 7, 1024, 1, 0x109d)]
    [InlineData(0, 0, 5, 7, 1024, 1, 0x109d)]
    [InlineData(0, 0, 6, 64000, 1024, 1, 0x109d)]
    [InlineData(0, 0, 6, 7, 11, 1, 0x109d)]
    [InlineData(0, 0, 6, 7, 1024, 2, 0x109d)]
    [InlineData(0, 0, 6, 7, 1024, 1, 0x109f)]
    public async Task RejectsIncompatibleAareAndRetainsItsDiagnostics(byte result, byte diagnostic,
        byte version, ushort vaa, ushort pdu, byte context, uint conformance)
    {
        var pair = DuplexApduChannel.CreatePair();
        await using var peer = new SessionPeer(pair.Server);
        await using var client = new DlmsClientSession(pair.Client, SessionPeer.Options());
        var associating = client.AssociateAsync(TestContext.Current.CancellationToken).AsTask();
        await peer.ReadAsync<AcseApdu>();
        var response = AcseApdu.Aare(new ApduCodec().EncodeBytes(new InitiateResponse(null, version, conformance, pdu, vaa)),
            context, result, diagnostic);
        await peer.SendAsync(response);
        var error = await Assert.ThrowsAsync<DlmsAssociationException>(async () => await associating);
        Assert.Equal(result, error.Response.Result);
        Assert.Equal(diagnostic, error.Response.Diagnostic!.Code);
        Assert.Equal(ClientSessionState.Faulted, client.State);
    }

    [Fact]
    public async Task RejectsIncomingPduAboveAdvertisedReceiveLimit()
    {
        var pair = DuplexApduChannel.CreatePair();
        await using var peer = new SessionPeer(pair.Server);
        var options = SessionPeer.Options() with
        {
            Association = new()
            {
                MaxReceivePduSize = 64
            }
        };
        await using var client = new DlmsClientSession(pair.Client, options);
        await peer.AssociateAsync(client);
        var reading = SessionPeer.GetAsync(client, TestContext.Current.CancellationToken).AsTask();
        var request = await peer.ReadAsync<LnRequest>();
        await peer.SendAsync(new LnResponse(EMCL.Core.Operations.CosemOperationKind.Read, request.InvokeIdAndPriority,
            EMCL.Core.Operations.OperationResult.Ok(DlmsValue.Octets(new byte[65]))));
        await Assert.ThrowsAsync<DlmsProtocolException>(async () => await reading);
    }

    [Fact]
    public async Task RejectsOversizedOutgoingOperationBeforeSendingAndKeepsSessionUsable()
    {
        var pair = DuplexApduChannel.CreatePair();
        await using var peer = new SessionPeer(pair.Server);
        await using var client = new DlmsClientSession(pair.Client, SessionPeer.Options());
        await peer.AssociateAsync(client, serverPdu: 16);
        await Assert.ThrowsAsync<ArgumentException>(async () => await client.SetAsync(CosemClassIds.Register,
            SessionPeer.RegisterName, 2, DlmsValue.Octets(new byte[100]), cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal(ClientSessionState.Associated, client.State);
        await peer.ReleaseAsync(client);
    }

    [Fact]
    public async Task SupportsInjectedCodecAndAcseStrategyFromAnotherAssembly()
    {
        var pair = DuplexApduChannel.CreatePair();
        await using var peer = new SessionPeer(pair.Server);
        var codec = new ObservedCodec();
        var negotiator = new CustomNegotiator();
        await using IDlmsClientSession client = new DlmsClientSession(pair.Client, SessionPeer.Options(), codec, negotiator);
        var associating = client.AssociateAsync(TestContext.Current.CancellationToken).AsTask();
        var request = await peer.ReadAsync<AcseApdu>();
        Assert.NotNull(request.Find(CustomNegotiator.CustomField));
        await peer.SendAsync(AcseApdu.Aare(new ApduCodec().EncodeBytes(new InitiateResponse(null,
            DlmsNegotiation.Version6, (uint)new LnAssociationOptions().Conformance, 1024))));
        await associating;
        var releasing = client.ReleaseAsync(TestContext.Current.CancellationToken).AsTask();
        Assert.Empty((await peer.ReadAsync<AcseApdu>()).Fields);
        await peer.SendAsync(AcseApdu.Release(response: true));
        await releasing;
        Assert.Equal(2, codec.Encoded);
        Assert.Equal(2, codec.Decoded);
    }

    private sealed class ObservedCodec : IIncrementalCodec<DlmsApdu>
    {
        private readonly ApduCodec inner = new();
        internal int Encoded
        {
            get; private set;
        }
        internal int Decoded
        {
            get; private set;
        }
        public DecodeResult<DlmsApdu> Decode(ReadOnlySequence<byte> input)
        {
            Decoded++;
            return inner.Decode(input);
        }

        public void Encode(DlmsApdu value, IBufferWriter<byte> output)
        {
            Encoded++;
            inner.Encode(value, output);
        }
    }

    private sealed class CustomNegotiator : IClientAssociationNegotiator
    {
        internal const byte CustomField = 0x8f;
        private readonly LnAssociationNegotiator inner = new();
        public AcseApdu CreateRequest(LnAssociationOptions proposal)
        {
            var request = inner.CreateRequest(proposal);
            return request with
            {
                Fields = request.Fields.Add(BerElement.Create(CustomField, 1))
            };
        }

        public NegotiatedAssociation AcceptResponse(AcseApdu response, LnAssociationOptions proposal) => inner.AcceptResponse(response, proposal);
        public AcseApdu CreateReleaseRequest() => AcseApdu.Release(reason: null);
        public void AcceptReleaseResponse(AcseApdu response) => inner.AcceptReleaseResponse(response);
    }
}
