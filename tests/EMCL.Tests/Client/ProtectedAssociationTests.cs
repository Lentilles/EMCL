using System.Buffers;
using EMCL.Client.Association;
using EMCL.Client.Sessions;
using EMCL.Core.Codecs;
using EMCL.Core.Objects;
using EMCL.Core.Operations;
using EMCL.Core.Values;
using EMCL.Protocol.Acse;
using EMCL.Protocol.Apdu;
using EMCL.Protocol.Security;
using EMCL.Tests.Protocol.Security;
using EMCL.Tests.Support;
using EMCL.Transport.Channels;

namespace EMCL.Tests.Client;

public sealed class ProtectedAssociationTests
{
    private static readonly ApduCodec Codec = new();

    [Theory]
    [InlineData(DlmsSecuritySuite.AesGcm128, "60857405080205")]
    [InlineData(DlmsSecuritySuite.Kuznyechik, "6085740508028100")]
    public async Task ExchangesProtectedInitiateHlsGetAndRelease(DlmsSecuritySuite suite, string expectedMechanism)
    {
        var pair = DuplexByteTransport.CreateWrapperPair();
        await using var channel = pair.Server;
        var clientProtocol = DlmsSecurityProtocolTests.Create(suite, dedicated: true);
        var server = DlmsSecurityProtocolTests.Create(suite, server: true, dedicated: true);
        var security = new CountingProtocol(clientProtocol);
        await using var client = new DlmsClientSession(pair.Client, SessionPeer.Options() with
        {
            Association = new LnAssociationOptions { Security = security }
        });
        var cancellationToken = TestContext.Current.CancellationToken;
        var associating = client.AssociateAsync(cancellationToken).AsTask();
        var request = Decode<AcseApdu>(await Receive(channel, cancellationToken));
        Assert.Equal(Convert.FromHexString(expectedMechanism), request.Find(AcseFieldTags.MechanismName)!.Content.ToArray());
        Assert.Equal(server.PeerSystemTitle, request.SystemTitle!.Value);
        Assert.Equal(0x21, request.UserInformation!.Value[0]);
        var initiate = Decode<InitiateRequest>(server.Unprotect(request.UserInformation.Value.AsSpan(), false));
        Assert.Equal(server.DedicatedKey, initiate.DedicatedKey);
        byte[] clientChallenge = request.CallingAuthenticationValue!.Value.ToArray();
        byte[] serverChallenge = server.CreateChallenge();
        byte[] accepted = Codec.EncodeBytes(new InitiateResponse(null, DlmsNegotiation.Version6, initiate.Conformance, 1024));
        var response = AcseApdu.Aare(server.Protect(accepted, false), server.ApplicationContext, 0,
            (byte)AcseServiceUserDiagnostic.AuthenticationRequired, server.AuthenticationMechanism, serverChallenge,
            server.LocalSystemTitle.AsSpan());
        await channel.SendAsync(Codec.EncodeBytes(response), cancellationToken);

        var hls = Decode<LnRequest>(server.Unprotect(await Receive(channel, cancellationToken), false));
        server.VerifyReply(serverChallenge, clientChallenge, hls.Operation.Value!.Bytes.AsSpan());
        var reply = DlmsValue.Octets(server.CreateReply(serverChallenge, clientChallenge));
        var hlsResponse = Codec.EncodeBytes(new LnResponse(CosemOperationKind.Invoke, hls.InvokeIdAndPriority, OperationResult.Ok(reply)));
        await channel.SendAsync(server.Protect(hlsResponse, false), cancellationToken);
        await associating;

        var get = client.GetAsync(CosemClassIds.Data, LogicalName.Parse("0.0.96.1.0.255"), 2,
            cancellationToken: cancellationToken).AsTask();
        byte[] encrypted = await Receive(channel, cancellationToken);
        Assert.Equal(0xd0, encrypted[0]);
        var getRequest = Decode<LnRequest>(server.Unprotect(encrypted, true));
        var value = DlmsValue.Octets([1, 2, 3]);
        var getResponse = Codec.EncodeBytes(new LnResponse(CosemOperationKind.Read, getRequest.InvokeIdAndPriority,
            OperationResult.Ok(value)));
        await channel.SendAsync(server.Protect(getResponse, true), cancellationToken);
        Assert.Equal(value.Bytes, (await get).Bytes);

        var releasing = client.ReleaseAsync(cancellationToken).AsTask();
        var release = Decode<AcseApdu>(await Receive(channel, cancellationToken));
        Assert.Equal(AcseKind.Rlrq, release.Kind);
        _ = Decode<InitiateRequest>(server.Unprotect(release.UserInformation!.Value.AsSpan(), true));
        var released = AcseApdu.Release(response: true);
        released = released with
        {
            Fields = [.. released.Fields, BerElement.Constructed(AcseFieldTags.UserInformation,
                BerElement.Create(4, server.Protect(accepted, true)))]
        };
        await channel.SendAsync(Codec.EncodeBytes(released), cancellationToken);
        await releasing;
        Assert.Equal(4, security.ProtectedCount);
        Assert.Equal(4, security.UnprotectedCount);
    }

    private static async Task<byte[]> Receive(IApduChannel channel, CancellationToken cancellationToken)
        => (await channel.ReceiveAsync(cancellationToken))!.Value.ToArray();

    private static T Decode<T>(byte[] payload) where T : DlmsApdu
    {
        var decoded = Codec.Decode(new ReadOnlySequence<byte>(payload));
        Assert.Equal(DecodeStatus.Done, decoded.Status);
        Assert.Equal(payload.Length, decoded.Consumed);
        return Assert.IsType<T>(decoded.Value);
    }

    private sealed class CountingProtocol(DlmsSecurityProtocol protocol) : ILnSecurityProtocol
    {
        public byte ApplicationContext => protocol.ApplicationContext;
        public byte AuthenticationMechanism => protocol.AuthenticationMechanism;
        public System.Collections.Immutable.ImmutableArray<byte> LocalSystemTitle => protocol.LocalSystemTitle;
        public System.Collections.Immutable.ImmutableArray<byte> PeerSystemTitle => protocol.PeerSystemTitle;
        public System.Collections.Immutable.ImmutableArray<byte> DedicatedKey => protocol.DedicatedKey;
        public int ProtectedCount
        {
            get; private set;
        }
        public int UnprotectedCount
        {
            get; private set;
        }
        public byte[] CreateChallenge() => protocol.CreateChallenge();
        public byte[] CreateReply(ReadOnlySpan<byte> localChallenge, ReadOnlySpan<byte> peerChallenge)
            => protocol.CreateReply(localChallenge, peerChallenge);
        public void VerifyReply(ReadOnlySpan<byte> localChallenge, ReadOnlySpan<byte> peerChallenge, ReadOnlySpan<byte> reply)
            => protocol.VerifyReply(localChallenge, peerChallenge, reply);
        public int GetProtectedLength(byte tag, int length, bool established) => protocol.GetProtectedLength(tag, length, established);
        public byte[] Protect(ReadOnlySpan<byte> apdu, bool established)
        {
            ProtectedCount++;
            return protocol.Protect(apdu, established);
        }

        public byte[] Unprotect(ReadOnlySpan<byte> apdu, bool established)
        {
            UnprotectedCount++;
            return protocol.Unprotect(apdu, established);
        }
    }
}
