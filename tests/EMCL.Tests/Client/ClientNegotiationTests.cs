using System.Collections.Immutable;
using System.Security.Cryptography;
using EMCL.Client.Association;
using EMCL.Client.Exceptions;
using EMCL.Client.Sessions;
using EMCL.Core.Codecs;
using EMCL.Core.Objects;
using EMCL.Core.Operations;
using EMCL.Core.Values;
using EMCL.Objects.Definitions;
using EMCL.Protocol.Acse;
using EMCL.Protocol.Apdu;
using EMCL.Tests.Support;
using EMCL.Transport.Channels;

namespace EMCL.Tests.Client;

public sealed class ClientNegotiationTests
{
    [Theory]
    [InlineData(null, 0x40, null, 0x41)]
    [InlineData((byte)0xC1, 0xC1, null, 0x40)]
    [InlineData((byte)0xC1, 0xC1, (byte)0xC1, 0xC1)]
    public async Task CompletesHighAuthenticationBeforeExposingAssociation(byte? hlsInvoke, byte expectedHlsInvoke,
        byte? fixedInvoke, byte firstGetInvoke)
    {
        byte[] secret = FromHex("000102030405060708090A0B0C0D0E0F");
        byte[] serverChallenge = FromHex("00112233445566778899AABBCCDDEEFF");
        var pair = DuplexApduChannel.CreatePair();
        await using var peer = new SessionPeer(pair.Server);
        await using var client = new DlmsClientSession(pair.Client, SessionPeer.Options() with
        {
            FixedInvokeIdAndPriority = fixedInvoke,
            Association = new()
            {
                Authentication = ClientAuthentication.High,
                Password = secret.ToImmutableArray(),
                HlsInvokeIdAndPriority = hlsInvoke
            }
        });

        var associating = client.AssociateAsync(TestContext.Current.CancellationToken).AsTask();
        var aarq = await peer.ReadAsync<AcseApdu>();
        Assert.Equal(2, aarq.Find(AcseFieldTags.MechanismName)!.Content[^1]);
        Assert.Equal(16, aarq.CallingAuthenticationValue!.Value.Length);
        Assert.False(associating.IsCompleted);
        await peer.SendAsync(HighAare(serverChallenge));
        var action = await peer.ReadAsync<LnRequest>();
        Assert.Equal(expectedHlsInvoke, action.InvokeIdAndPriority);
        Assert.Equal(CosemOperationKind.Invoke, action.Operation.Kind);
        Assert.Equal(CosemClassIds.AssociationLn, action.Operation.ClassId);
        Assert.Equal(AssociationLnMembers.Methods.ReplyToHlsAuthentication, action.Operation.Index);
        AssertBytesEqual(FromHex("69C4E0D86A7B0430D8CDB78070B4C55A"), action.Operation.Value!.Bytes.AsSpan());
        Assert.Equal(ClientSessionState.Associating, client.State);

        using var aes = Aes.Create();
        aes.Key = secret;
        byte[] serverReply = aes.EncryptEcb(aarq.CallingAuthenticationValue.Value.AsSpan(), PaddingMode.None);
        await peer.SendAsync(new LnResponse(CosemOperationKind.Invoke, expectedHlsInvoke,
            OperationResult.Ok(DlmsValue.Octets(serverReply))));
        var negotiated = await associating;
        Assert.Equal(ClientSessionState.Associated, client.State);
        Assert.Equal((DlmsConformance)0x00189D, negotiated.Conformance);
        Assert.Equal(384, negotiated.ServerMaxReceivePduSize);

        for (int offset = 0; offset < 2; offset++)
        {
            byte expectedGetInvoke = fixedInvoke ?? (byte)(firstGetInvoke + offset);
            var reading = SessionPeer.GetAsync(client, TestContext.Current.CancellationToken).AsTask();
            var get = await peer.ReadAsync<LnRequest>();
            Assert.Equal(CosemOperationKind.Read, get.Operation.Kind);
            Assert.Equal(expectedGetInvoke, get.InvokeIdAndPriority);
            await peer.SendAsync(new LnResponse(CosemOperationKind.Read, expectedGetInvoke, OperationResult.Ok(DlmsValue.Null)));
            Assert.Equal(DlmsType.Null, (await reading).Type);
        }

        await peer.ReleaseAsync(client);
    }

    [Theory]
    [InlineData(null, 0x40, 0x41)]
    [InlineData(null, 0x40, 0xC0)]
    [InlineData(null, 0x40, 0xC1)]
    [InlineData((byte)0xC1, 0xC1, 0xC0)]
    [InlineData((byte)0xC1, 0xC1, 0x41)]
    public async Task RejectsAnHlsInvokeMismatchWithTheStageAndBothOctets(byte? hlsInvoke, byte expected, byte received)
    {
        var pair = DuplexApduChannel.CreatePair();
        await using var peer = new SessionPeer(pair.Server);
        await using var client = new DlmsClientSession(pair.Client, SessionPeer.Options() with
        {
            Association = new()
            {
                Authentication = ClientAuthentication.High,
                Password = new byte[16].ToImmutableArray(),
                HlsInvokeIdAndPriority = hlsInvoke
            }
        });
        var associating = client.AssociateAsync(TestContext.Current.CancellationToken).AsTask();
        var aarq = await peer.ReadAsync<AcseApdu>();
        await peer.SendAsync(HighAare(new byte[16]));
        var action = await peer.ReadAsync<LnRequest>();
        Assert.Equal(expected, action.InvokeIdAndPriority);

        using var aes = Aes.Create();
        aes.Key = new byte[16];
        var reply = aes.EncryptEcb(aarq.CallingAuthenticationValue!.Value.AsSpan(), PaddingMode.None);
        await peer.SendAsync(new LnResponse(CosemOperationKind.Invoke, received, OperationResult.Ok(DlmsValue.Octets(reply))));

        var error = await Assert.ThrowsAsync<DlmsProtocolException>(async () => await associating);
        Assert.Contains("stage=HLS ACTION", error.Message, StringComparison.Ordinal);
        Assert.Contains($"expected=0x{expected:X2}, received=0x{received:X2}", error.Message, StringComparison.Ordinal);
        Assert.Equal(ClientSessionState.Faulted, client.State);
    }

    [Theory]
    [InlineData(null, 0x40)]
    [InlineData((byte)0xC1, 0xC1)]
    public async Task RejectsIncorrectHighAuthenticationReply(byte? hlsInvoke, byte expectedHlsInvoke)
    {
        var pair = DuplexApduChannel.CreatePair();
        await using var peer = new SessionPeer(pair.Server);
        await using var client = new DlmsClientSession(pair.Client, SessionPeer.Options() with
        {
            Association = new()
            {
                Authentication = ClientAuthentication.High,
                Password = new byte[16].ToImmutableArray(),
                HlsInvokeIdAndPriority = hlsInvoke
            }
        });
        var associating = client.AssociateAsync(TestContext.Current.CancellationToken).AsTask();
        var aarq = await peer.ReadAsync<AcseApdu>();
        await peer.SendAsync(HighAare(new byte[16]));
        var action = await peer.ReadAsync<LnRequest>();
        Assert.Equal(expectedHlsInvoke, action.InvokeIdAndPriority);
        using var aes = Aes.Create();
        aes.Key = new byte[16];
        var reply = aes.EncryptEcb(aarq.CallingAuthenticationValue!.Value.AsSpan(), PaddingMode.None);
        reply[0] ^= 1;
        await peer.SendAsync(new LnResponse(CosemOperationKind.Invoke, expectedHlsInvoke,
            OperationResult.Ok(DlmsValue.Octets(reply))));
        var error = await Assert.ThrowsAsync<DlmsAssociationException>(async () => await associating);
        Assert.Contains("HLS challenge response is invalid", error.Message, StringComparison.Ordinal);
        Assert.Equal(ClientSessionState.Faulted, client.State);
    }

    [Theory]
    [InlineData(0x81)]
    [InlineData(0x51)]
    [InlineData(0xE1)]
    public void RejectsAnUnconfirmedHlsServiceOrNonzeroReservedBits(byte invoke)
    {
        var options = new LnAssociationOptions
        {
            Authentication = ClientAuthentication.High,
            Password = new byte[16].ToImmutableArray(),
            HlsInvokeIdAndPriority = invoke
        };
        var negotiator = new LnAssociationNegotiator();
        Assert.Throws<ArgumentOutOfRangeException>(nameof(LnAssociationOptions.HlsInvokeIdAndPriority),
            () => negotiator.CreateRequest(options));
    }

    [Fact]
    public async Task FixedInvokeIdAndPriorityRequiresSequentialRequests()
    {
        var pair = DuplexApduChannel.CreatePair();
        await using var clientChannel = pair.Client;
        await using var peerChannel = pair.Server;
        Assert.Throws<ArgumentException>(nameof(ClientSessionOptions.MaxConcurrentRequests), () =>
            new DlmsClientSession(clientChannel, SessionPeer.Options(concurrency: 2) with
            {
                FixedInvokeIdAndPriority = 0xC1
            }));
    }

    private static AcseApdu HighAare(byte[] serverChallenge)
    {
        var response = AcseApdu.Aare(new ApduCodec().EncodeBytes(new InitiateResponse(null,
            DlmsNegotiation.Version6, 0x00189D, 384)),
            diagnostic: (byte)AcseServiceUserDiagnostic.AuthenticationRequired);
        return response with
        {
            Fields = response.Fields.Add(BerElement.Constructed(AcseFieldTags.RespondingAuthenticationValue,
                BerElement.Create(AcseFieldTags.AuthenticationGraphicString, serverChallenge)))
        };
    }

    [Fact]
    public async Task EncodesTheConfirmedLnProposalAndValidatesNegotiatedParameters()
    {
        var pair = DuplexApduChannel.CreatePair();
        await using var peer = new SessionPeer(pair.Server);
        await using var client = new DlmsClientSession(pair.Client, SessionPeer.Options());
        var request = await peer.AssociateAsync(client);
        AssertBytesEqual(FromHex("01000000065F1F040000189D0400"), request.UserInformation!.Value.AsSpan());
        AssertBytesEqual(FromHex("60857405080101"), request.ApplicationContext!.Value.AsSpan());
        await peer.ReleaseAsync(client);
    }

    [Fact]
    public async Task CanExplicitlyIncludeResponseAllowedWhenRequested()
    {
        var pair = DuplexApduChannel.CreatePair();
        await using var peer = new SessionPeer(pair.Server);
        await using var client = new DlmsClientSession(pair.Client, SessionPeer.Options() with
        {
            Association = new LnAssociationOptions { ResponseAllowed = true }
        });
        var request = await peer.AssociateAsync(client);
        AssertBytesEqual(FromHex("0100010100065F1F040000189D0400"), request.UserInformation!.Value.AsSpan());
        await peer.ReleaseAsync(client);
    }

    [Fact]
    public void CanAdvertiseObservedFullConformanceWhileRetainingImplementedServiceSubset()
    {
        var observedServices = DlmsConformance.GeneralProtection | DlmsConformance.GeneralBlockTransfer
            | DlmsConformance.DeltaValueEncoding | DlmsConformance.BlockTransferWithGet
            | DlmsConformance.BlockTransferWithSet | DlmsConformance.BlockTransferWithAction
            | DlmsConformance.MultipleReferences | DlmsConformance.DataNotification
            | DlmsConformance.Access | DlmsConformance.Get | DlmsConformance.Set
            | DlmsConformance.SelectiveAccess | DlmsConformance.EventNotification | DlmsConformance.Action;
        Assert.Equal((DlmsConformance)0x621EDF, observedServices);

        var proposal = new LnAssociationOptions
        {
            Conformance = DlmsConformance.Get | DlmsConformance.Action | DlmsConformance.BlockTransferWithGet,
            AdvertisedConformance = observedServices,
            MaxReceivePduSize = ushort.MaxValue
        };
        var negotiator = new LnAssociationNegotiator();
        var request = negotiator.CreateRequest(proposal);
        AssertBytesEqual(FromHex("01000000065F1F0400621EDFFFFF"), request.UserInformation!.Value.AsSpan());

        var response = AcseApdu.Aare(new ApduCodec().EncodeBytes(new InitiateResponse(null,
            DlmsNegotiation.Version6, 0x001010, 2048)));
        var negotiated = negotiator.AcceptResponse(response, proposal);
        Assert.Equal(DlmsConformance.Get | DlmsConformance.BlockTransferWithGet, negotiated.Conformance);
    }

    [Fact]
    public async Task ExplicitlyPermittedUnexpectedSetBlockBitIsNotExposedAsImplemented()
    {
        var pair = DuplexApduChannel.CreatePair();
        await using var peer = new SessionPeer(pair.Server);
        await using var client = new DlmsClientSession(pair.Client, SessionPeer.Options() with
        {
            Association = new LnAssociationOptions
            {
                Conformance = new LnAssociationOptions().Conformance & ~DlmsConformance.BlockTransferWithSet,
                AcceptUnexpectedBlockTransferWithSet = true
            }
        });
        var request = await peer.AssociateAsync(client, (DlmsConformance)0x00189D, serverPdu: 2048);
        Assert.NotNull(request);
        Assert.Equal((DlmsConformance)0x00109D, client.NegotiatedParameters!.Conformance);
        await peer.ReleaseAsync(client);
    }

    [Fact]
    public async Task AssociationRetrySettingNeverRetriesGet()
    {
        var pair = DuplexApduChannel.CreatePair();
        await using var peer = new SessionPeer(pair.Server);
        var channel = new RetryCountingChannel(pair.Client);
        await using var client = new DlmsClientSession(channel, SessionPeer.Options() with
        {
            AssociationRetryDelay = TimeSpan.FromMilliseconds(20),
            RequestTimeout = TimeSpan.FromMilliseconds(200)
        });
        await peer.AssociateAsync(client);
        var reading = SessionPeer.GetAsync(client, TestContext.Current.CancellationToken).AsTask();
        await peer.ReadAsync<LnRequest>();
        await Assert.ThrowsAsync<TimeoutException>(async () => await reading);
        Assert.Equal(0, channel.RetryCount);
    }

    [Theory]
    [InlineData(1, 0, 6, 7, 1024, 1, 0x109d)]
    [InlineData(0, 14, 6, 7, 1024, 1, 0x109d)]
    [InlineData(0, 0, 5, 7, 1024, 1, 0x109d)]
    [InlineData(0, 0, 6, 64000, 1024, 1, 0x109d)]
    [InlineData(0, 0, 6, 7, 11, 1, 0x109d)]
    [InlineData(0, 0, 6, 7, 1024, 2, 0x109d)]
    [InlineData(0, 0, 6, 7, 1024, 1, 0x109f)]
    [InlineData(0, 0, 6, 7, 1024, 1, 0x149d)]
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
    public async Task ReportsAuthenticationMechanismDiagnosticWithoutExposingCredentials()
    {
        var pair = DuplexApduChannel.CreatePair();
        await using var peer = new SessionPeer(pair.Server);
        await using var client = new DlmsClientSession(pair.Client, SessionPeer.Options() with
        {
            Association = new()
            {
                Authentication = ClientAuthentication.Low,
                Password = ImmutableArray.CreateRange("secret"u8.ToArray())
            }
        });
        var associating = client.AssociateAsync(TestContext.Current.CancellationToken).AsTask();
        await peer.ReadAsync<AcseApdu>();
        await peer.SendAsync(AcseApdu.Aare([], result: 1,
            diagnostic: (byte)AcseServiceUserDiagnostic.AuthenticationMechanismNameNotRecognized));

        var error = await Assert.ThrowsAsync<DlmsAssociationException>(async () => await associating);
        Assert.Contains("diagnostic=11 (AuthenticationMechanismNameNotRecognized)", error.Message,
            StringComparison.Ordinal);
        Assert.DoesNotContain("secret", error.Message, StringComparison.Ordinal);
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

    private sealed class RetryCountingChannel(IApduChannel inner) : IApduChannel, IAssociationRequestRetransmission
    {
        public int RetryCount
        {
            get; private set;
        }
        public ValueTask<ReadOnlyMemory<byte>?> ReceiveAsync(CancellationToken cancellationToken = default) =>
            inner.ReceiveAsync(cancellationToken);
        public ValueTask SendAsync(ReadOnlyMemory<byte> apdu, CancellationToken cancellationToken = default) =>
            inner.SendAsync(apdu, cancellationToken);
        public ValueTask<bool> RetryUnansweredAssociationRequestAsync(CancellationToken cancellationToken = default)
        {
            RetryCount++;
            return ValueTask.FromResult(false);
        }
        public ValueTask DisposeAsync() => inner.DisposeAsync();
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
