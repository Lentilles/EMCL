using EMCL.Client.Association;
using EMCL.Client.Exceptions;
using EMCL.Client.Sessions;
using EMCL.Composition;
using EMCL.Core.Access;
using EMCL.Core.Codecs;
using EMCL.Core.Objects;
using EMCL.Core.Operations;
using EMCL.Core.Values;
using EMCL.Messaging;
using EMCL.Objects.Association;
using EMCL.Objects.Definitions;
using EMCL.Protocol.Acse;
using EMCL.Protocol.Apdu;
using EMCL.Protocol.Axdr;
using EMCL.Tests.Support;
using EMCL.Transport.Channels;

namespace EMCL.Tests.Client;

public sealed class ClientSessionTests
{
    [Theory]
    [InlineData(false, 3)]
    [InlineData(true, 1)]
    public async Task RunsAssociationOperationsCatalogImportAndReleaseAgainstLocalPipeline(bool wrapper, byte version)
    {
        var pair = CreatePair(wrapper);
        await using var peer = new SessionPeer(pair.Server);
        await using var client = new DlmsClientSession(pair.Client, SessionPeer.Options());
        var device = new EmclBuilder().Build("server");
        var register = device.Add(CosemClassIds.Register, RegisterMembers.Version0, SessionPeer.RegisterName);
        register.State.Set(RegisterMembers.Attributes.Value, Number(DlmsType.UInt32, 42));
        var association = device.Add(CosemClassIds.AssociationLn, version, LogicalName.Parse("0.0.40.0.0.255"));
        byte mode = version == AssociationLnMembers.Version1 ? (byte)AttributeAccessModeV1.ReadWrite : (byte)(AccessModeV3.Read | AccessModeV3.Write);
        device.Associations.Bind(association.LogicalName, new("public", version,
            ImmutableDictionary<LogicalName, ObjectAccessRights>.Empty.Add(register.LogicalName,
                new([new(RegisterMembers.Attributes.Value, mode, [])], [new(RegisterMembers.Methods.Reset, 1)]))));
        var server = ServeAsync();
        var negotiated = await client.AssociateAsync(TestContext.Current.CancellationToken);
        Assert.Equal(ClientSessionState.Associated, client.State);
        Assert.Equal(DlmsNegotiation.Version6, negotiated.DlmsVersion);
        Assert.Equal(1024, negotiated.ServerMaxReceivePduSize);
        Assert.Equal(42UL, (await SessionPeer.GetAsync(client, TestContext.Current.CancellationToken)).AsUInt64());
        await client.SetAsync(CosemClassIds.Register, register.LogicalName, RegisterMembers.Attributes.Value,
            Number(DlmsType.UInt32, 99), cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(99UL, ReadStoredValue(register, RegisterMembers.Attributes.Value).AsUInt64());
        Assert.Null(await client.ActionAsync(CosemClassIds.Register, register.LogicalName, RegisterMembers.Methods.Reset,
            Number(DlmsType.Int8, 0), TestContext.Current.CancellationToken));
        Assert.Equal(0UL, (await SessionPeer.GetAsync(client, TestContext.Current.CancellationToken)).AsUInt64());
        var imported = await client.LoadCatalogAsync(new EmclBuilder().CreateImporter(), "imported", "public", version,
            cancellationToken: TestContext.Current.CancellationToken);
        var importedRegister = Assert.Single(imported.Catalog.Objects);
        Assert.True(importedRegister.Descriptor.IsKnown);
        Assert.False(importedRegister.State.TryGet(RegisterMembers.Attributes.Value, out _));
        Assert.Equal(mode, Assert.Single(imported.Access.Rights[register.LogicalName].Attributes).RawMode);
        await client.ReleaseAsync(TestContext.Current.CancellationToken);
        Assert.Equal(ClientSessionState.Released, client.State);
        await client.ReleaseAsync(TestContext.Current.CancellationToken);
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await SessionPeer.GetAsync(client, TestContext.Current.CancellationToken));
        await server;

        async Task ServeAsync()
        {
            while (true)
            {
                var bytes = await pair.Server.ReceiveAsync(TestContext.Current.CancellationToken);
                Assert.True(bytes.HasValue);
                var decoded = new ApduCodec().Decode(new(bytes.Value));
                switch (decoded.Value)
                {
                    case AcseApdu { Kind: AcseKind.Aarq }:
                        await peer.SendAsync(AcseApdu.Aare(new ApduCodec().EncodeBytes(new InitiateResponse(null,
                            DlmsNegotiation.Version6, (uint)(LnAssociationOptions.SupportedConformance & ~DlmsConformance.PriorityManagement), 1024))));
                        break;
                    case LnRequest request:
                        await peer.SendAsync(await DlmsEndpoint.ExecuteAsync(request, device.Pipeline, device.LocalContext,
                            TestContext.Current.CancellationToken));
                        break;
                    case AcseApdu { Kind: AcseKind.Rlrq }:
                        await peer.SendAsync(AcseApdu.Release(response: true));
                        return;
                    default:
                        throw new InvalidOperationException("Unexpected test-server request.");
                }
            }
        }
    }

    [Fact]
    public async Task CorrelatesReversedConcurrentRepliesAndRoutesNotifications()
    {
        var pair = DuplexApduChannel.CreatePair();
        await using var peer = new SessionPeer(pair.Server);
        await using var client = new DlmsClientSession(pair.Client, SessionPeer.Options(concurrency: 2));
        await peer.AssociateAsync(client);
        var first = SessionPeer.GetAsync(client, TestContext.Current.CancellationToken).AsTask();
        var second = SessionPeer.GetAsync(client, TestContext.Current.CancellationToken).AsTask();
        var request1 = await peer.ReadAsync<LnRequest>();
        var request2 = await peer.ReadAsync<LnRequest>();
        Assert.NotEqual(request1.InvokeIdAndPriority, request2.InvokeIdAndPriority);
        await peer.SendAsync(new DataNotification(1, [], Number(DlmsType.UInt16, 700)));
        await peer.SendAsync(new LnResponse(CosemOperationKind.Read, request2.InvokeIdAndPriority, OperationResult.Ok(Number(DlmsType.UInt32, 20))));
        await peer.SendAsync(new LnResponse(CosemOperationKind.Read, request1.InvokeIdAndPriority, OperationResult.Ok(Number(DlmsType.UInt32, 10))));
        Assert.Equal(10UL, (await first).AsUInt64());
        Assert.Equal(20UL, (await second).AsUInt64());
        await using var notifications = client.ReadNotificationsAsync(TestContext.Current.CancellationToken).GetAsyncEnumerator(TestContext.Current.CancellationToken);
        Assert.True(await notifications.MoveNextAsync());
        Assert.Equal(700UL, notifications.Current.Body.AsUInt64());
        await peer.ReleaseAsync(client);
        Assert.False(await notifications.MoveNextAsync());
    }

    [Fact]
    public async Task KeepsRemoteErrorsSeparateFromProtocolFailureAndPreservesActionReturnDataError()
    {
        var pair = DuplexApduChannel.CreatePair();
        await using var peer = new SessionPeer(pair.Server);
        await using var client = new DlmsClientSession(pair.Client, SessionPeer.Options());
        await peer.AssociateAsync(client);
        var reading = SessionPeer.GetAsync(client, TestContext.Current.CancellationToken).AsTask();
        var request = await peer.ReadAsync<LnRequest>();
        await peer.SendAsync(new LnResponse(CosemOperationKind.Read, request.InvokeIdAndPriority, new(CosemResultCode.ReadWriteDenied)));
        var denied = await Assert.ThrowsAsync<DlmsOperationException>(async () => await reading);
        Assert.Equal(CosemResultCode.ReadWriteDenied, denied.Error);
        Assert.Equal(ClientSessionState.Associated, client.State);
        var action = client.ActionAsync(CosemClassIds.Register, SessionPeer.RegisterName, RegisterMembers.Methods.Reset,
            Number(DlmsType.Int8, 0), TestContext.Current.CancellationToken).AsTask();
        request = await peer.ReadAsync<LnRequest>();
        await peer.SendAsync(new LnResponse(CosemOperationKind.Invoke, request.InvokeIdAndPriority,
            OperationResult.Ok(), CosemResultCode.ObjectUnavailable));
        var failedReturn = await Assert.ThrowsAsync<DlmsOperationException>(async () => await action);
        Assert.Equal(CosemResultCode.Success, failedReturn.Response.Result.Error);
        Assert.Equal(CosemResultCode.ObjectUnavailable, failedReturn.Error);
        await peer.ReleaseAsync(client);
    }

    [Fact]
    public async Task EnforcesNegotiatedServicesSelectiveAccessAndAsymmetricPduLimitsBeforeSending()
    {
        var pair = DuplexApduChannel.CreatePair();
        await using var peer = new SessionPeer(pair.Server);
        await using var client = new DlmsClientSession(pair.Client, SessionPeer.Options());
        await peer.AssociateAsync(client, DlmsConformance.Get, serverPdu: 16);
        await Assert.ThrowsAsync<NotSupportedException>(async () => await client.SetAsync(CosemClassIds.Register,
            SessionPeer.RegisterName, 2, Number(DlmsType.UInt32, 1), cancellationToken: TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<NotSupportedException>(async () => await client.GetAsync(CosemClassIds.Register,
            SessionPeer.RegisterName, 2, new(1, DlmsValue.Null), TestContext.Current.CancellationToken));
        var read = SessionPeer.GetAsync(client, TestContext.Current.CancellationToken).AsTask();
        var request = await peer.ReadAsync<LnRequest>();
        // A server's small receive limit does not limit its outgoing responses.
        await peer.SendAsync(new LnResponse(CosemOperationKind.Read, request.InvokeIdAndPriority,
            OperationResult.Ok(DlmsValue.Octets(new byte[100]))));
        Assert.Equal(100, (await read).Bytes.Length);
        await peer.ReleaseAsync(client);
    }

    [Fact]
    public async Task SendsLowLevelAuthenticationAndAcceptsUnlimitedServerPduSize()
    {
        var pair = DuplexApduChannel.CreatePair();
        await using var peer = new SessionPeer(pair.Server);
        var options = SessionPeer.Options() with
        {
            Association = new()
            {
                Authentication = ClientAuthentication.Low,
                Password = new byte[] { 1, 2, 3 }.ToImmutableArray()
            }
        };
        await using var client = new DlmsClientSession(pair.Client, options);
        var request = await peer.AssociateAsync(client, serverPdu: DlmsNegotiation.UnlimitedPduSize);
        AssertBytesEqual([0x80, 3, 1, 2, 3], request.Find(AcseFieldTags.CallingAuthenticationValue)!.Content.AsSpan());
        Assert.Equal(DlmsNegotiation.UnlimitedPduSize, client.NegotiatedParameters!.ServerMaxReceivePduSize);
        await peer.ReleaseAsync(client);
    }

    private static (IApduChannel Client, IApduChannel Server) CreatePair(bool wrapper)
    {
        if (wrapper)
        {
            return DuplexByteTransport.CreateWrapperPair();
        }

        var pair = DuplexApduChannel.CreatePair();
        return (pair.Client, pair.Server);
    }
}
