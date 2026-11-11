using EMCL.Client.Exceptions;
using EMCL.Client.Sessions;
using EMCL.Composition;
using EMCL.Core.Codecs;
using EMCL.Core.Objects;
using EMCL.Core.Operations;
using EMCL.Core.Values;
using EMCL.Objects.Definitions;
using EMCL.Protocol.Apdu;
using EMCL.Protocol.Axdr;
using EMCL.Tests.Support;

namespace EMCL.Tests.Client;

public sealed class ClientBlockTransferTests
{
    [Theory]
    [InlineData(3, false)]
    [InlineData(1, false)]
    [InlineData(3, true)]
    public async Task ImportsKnownAndFutureObjectsFromAnAutomaticallyAssembledCatalog(byte version, bool ignoreInvokeIdValidation)
    {
        var pair = DuplexApduChannel.CreatePair();
        await using var peer = new SessionPeer(pair.Server);
        await using var client = new DlmsClientSession(pair.Client, SessionPeer.Options() with
        {
            IgnoreInvokeIdValidation = ignoreInvokeIdValidation
        });
        await peer.AssociateAsync(client);
        var importing = client.LoadCatalogAsync(new EmclBuilder().CreateImporter(), "meter", "public", version,
            cancellationToken: TestContext.Current.CancellationToken).AsTask();
        var request = await peer.ReadAsync<LnRequest>();
        Assert.Equal(CosemClassIds.AssociationLn, request.Operation.ClassId);
        Assert.Equal(AssociationLnMembers.Attributes.ObjectList, request.Operation.Index);
        var list = DlmsValue.Array(Entry(4, 0, "1.0.1.8.0.255"), Entry(9000, 9, "0.0.96.1.0.255"));
        var bytes = new AxdrCodec().EncodeBytes(list);
        int middle = bytes.Length / 2;
        byte firstResponseInvoke = ignoreInvokeIdValidation ? (byte)0xC1 : request.InvokeIdAndPriority;
        await peer.SendAsync(new GetBlockResponse(firstResponseInvoke, false, 1, bytes[..middle].ToImmutableArray()));
        var next = await peer.ReadAsync<GetNextRequest>();
        Assert.Equal(request.InvokeIdAndPriority, next.InvokeIdAndPriority);
        byte lastResponseInvoke = ignoreInvokeIdValidation ? (byte)0x42 : request.InvokeIdAndPriority;
        await peer.SendAsync(new GetBlockResponse(lastResponseInvoke, true, 2, bytes[middle..].ToImmutableArray()));
        var imported = await importing;
        Assert.Equal(2, imported.Catalog.Objects.Length);
        Assert.True(imported.Catalog.Find(SessionPeer.RegisterName)!.Descriptor.IsKnown);
        var future = imported.Catalog.Find(LogicalName.Parse("0.0.96.1.0.255"))!;
        Assert.False(future.Descriptor.IsKnown);
        Assert.Equal((byte)9, future.Descriptor.Key.ClassVersion);
    }

    [Fact]
    public async Task BoundsAccumulatedBytesIndependentlyOfIndividualPduSizes()
    {
        var pair = DuplexApduChannel.CreatePair();
        await using var peer = new SessionPeer(pair.Server);
        var options = SessionPeer.Options() with
        {
            Limits = new()
            {
                MaxMessageBytes = 64
            },
            Association = new()
            {
                MaxReceivePduSize = 64
            }
        };
        await using var client = new DlmsClientSession(pair.Client, options);
        await peer.AssociateAsync(client);
        var reading = SessionPeer.GetAsync(client, TestContext.Current.CancellationToken).AsTask();
        var request = await peer.ReadAsync<LnRequest>();
        await peer.SendAsync(new GetBlockResponse(request.InvokeIdAndPriority, false, 1, new byte[40].ToImmutableArray()));
        await peer.ReadAsync<GetNextRequest>();
        await peer.SendAsync(new GetBlockResponse(request.InvokeIdAndPriority, true, 2, new byte[40].ToImmutableArray()));
        await Assert.ThrowsAsync<DlmsProtocolException>(async () => await reading);
        Assert.Equal(ClientSessionState.Faulted, client.State);
    }

    [Fact]
    public async Task UsesOneDeadlineAcrossAllGetBlocks()
    {
        var pair = DuplexApduChannel.CreatePair();
        await using var peer = new SessionPeer(pair.Server);
        var clock = new ControlledDeadlineProvider();
        await using var client = new DlmsClientSession(pair.Client, SessionPeer.Options() with
        {
            TimeProvider = clock
        });
        await peer.AssociateAsync(client);
        var reading = SessionPeer.GetAsync(client, TestContext.Current.CancellationToken).AsTask();
        var request = await peer.ReadAsync<LnRequest>();
        await peer.SendAsync(new GetBlockResponse(request.InvokeIdAndPriority, false, 1, [9]));
        await peer.ReadAsync<GetNextRequest>();
        clock.ExpireDeadlines();
        await Assert.ThrowsAsync<TimeoutException>(async () => await reading);
        Assert.Equal(ClientSessionState.Faulted, client.State);
    }

    [Fact]
    public async Task RejectsUnnegotiatedBlockTransfer()
    {
        var pair = DuplexApduChannel.CreatePair();
        await using var peer = new SessionPeer(pair.Server);
        await using var client = new DlmsClientSession(pair.Client, SessionPeer.Options());
        await peer.AssociateAsync(client, DlmsConformance.Get);
        var reading = SessionPeer.GetAsync(client, TestContext.Current.CancellationToken).AsTask();
        var request = await peer.ReadAsync<LnRequest>();
        await peer.SendAsync(new GetBlockResponse(request.InvokeIdAndPriority, true, 1, [0]));
        await Assert.ThrowsAsync<DlmsProtocolException>(async () => await reading);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AssemblesGetBlocksAndAcknowledgesTheLastReceivedNumber(bool wrapper)
    {
        var memory = DuplexApduChannel.CreatePair();
        var pair = wrapper ? DuplexByteTransport.CreateWrapperPair() : (Client: (EMCL.Transport.Channels.IApduChannel)memory.Client, Server: memory.Server);
        await using var peer = new SessionPeer(pair.Server);
        await using var client = new DlmsClientSession(pair.Client, SessionPeer.Options());
        await peer.AssociateAsync(client);
        var reading = SessionPeer.GetAsync(client, TestContext.Current.CancellationToken).AsTask();
        var request = await peer.ReadAsync<LnRequest>();
        var bytes = FromHex("0906010203040506");
        await peer.SendAsync(new GetBlockResponse(request.InvokeIdAndPriority, false, 1, bytes[..3].ToImmutableArray()));
        var next = await peer.ReadAsync<GetNextRequest>();
        Assert.Equal(1u, next.BlockNumber);
        AssertBytesEqual(FromHex("C0024000000001"), new ApduCodec().EncodeBytes(next));
        await peer.SendAsync(new GetBlockResponse(request.InvokeIdAndPriority, false, 2, bytes[3..5].ToImmutableArray()));
        next = await peer.ReadAsync<GetNextRequest>();
        Assert.Equal(2u, next.BlockNumber);
        await peer.SendAsync(new GetBlockResponse(request.InvokeIdAndPriority, true, 3, bytes[5..].ToImmutableArray()));
        var value = await reading;
        AssertBytesEqual(FromHex("010203040506"), value.Bytes.AsSpan());
        await peer.ReleaseAsync(client);
    }

    [Fact]
    public async Task IsolatesTwoInterleavedTransfers()
    {
        var pair = DuplexApduChannel.CreatePair();
        await using var peer = new SessionPeer(pair.Server);
        await using var client = new DlmsClientSession(pair.Client, SessionPeer.Options(concurrency: 2));
        await peer.AssociateAsync(client);
        var first = SessionPeer.GetAsync(client, TestContext.Current.CancellationToken).AsTask();
        var second = SessionPeer.GetAsync(client, TestContext.Current.CancellationToken).AsTask();
        var request1 = await peer.ReadAsync<LnRequest>();
        var request2 = await peer.ReadAsync<LnRequest>();
        await peer.SendAsync(new GetBlockResponse(request1.InvokeIdAndPriority, false, 1, FromHex("060000").ToImmutableArray()));
        await peer.SendAsync(new GetBlockResponse(request2.InvokeIdAndPriority, false, 1, FromHex("060000").ToImmutableArray()));
        var next1 = await peer.ReadAsync<GetNextRequest>();
        var next2 = await peer.ReadAsync<GetNextRequest>();
        Assert.NotEqual(next1.InvokeIdAndPriority, next2.InvokeIdAndPriority);
        await peer.SendAsync(new GetBlockResponse(request2.InvokeIdAndPriority, true, 2, FromHex("0014").ToImmutableArray()));
        await peer.SendAsync(new GetBlockResponse(request1.InvokeIdAndPriority, true, 2, FromHex("000A").ToImmutableArray()));
        Assert.Equal(10UL, (await first).AsUInt64());
        Assert.Equal(20UL, (await second).AsUInt64());
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(2, false)]
    [InlineData(2, true)]
    public async Task RejectsInvalidFirstBlockNumbers(uint number, bool ignoreInvokeIdValidation)
    {
        var pair = DuplexApduChannel.CreatePair();
        await using var peer = new SessionPeer(pair.Server);
        await using var client = new DlmsClientSession(pair.Client, SessionPeer.Options() with
        {
            IgnoreInvokeIdValidation = ignoreInvokeIdValidation
        });
        await peer.AssociateAsync(client);
        var reading = SessionPeer.GetAsync(client, TestContext.Current.CancellationToken).AsTask();
        var request = await peer.ReadAsync<LnRequest>();
        byte responseInvoke = ignoreInvokeIdValidation ? (byte)0xC1 : request.InvokeIdAndPriority;
        await peer.SendAsync(new GetBlockResponse(responseInvoke, true, number, [0]));
        var error = await Assert.ThrowsAsync<DlmsProtocolException>(async () => await reading);
        Assert.Contains("block number is out of sequence", error.Message, StringComparison.Ordinal);
        Assert.Equal(ClientSessionState.Faulted, client.State);
    }

    [Fact]
    public async Task RejectsRepeatedBlocksAndOverLimitTransfers()
    {
        foreach (bool repeat in new[] { false, true })
        {
            var pair = DuplexApduChannel.CreatePair();
            await using var peer = new SessionPeer(pair.Server);
            var options = SessionPeer.Options() with
            {
                Limits = new()
                {
                    MaxBlockCount = 1
                }
            };
            await using var client = new DlmsClientSession(pair.Client, options);
            await peer.AssociateAsync(client);
            var reading = SessionPeer.GetAsync(client, TestContext.Current.CancellationToken).AsTask();
            var request = await peer.ReadAsync<LnRequest>();
            await peer.SendAsync(new GetBlockResponse(request.InvokeIdAndPriority, false, 1, [9]));
            await peer.ReadAsync<GetNextRequest>();
            await peer.SendAsync(new GetBlockResponse(request.InvokeIdAndPriority, true, repeat ? 1u : 2u, [0]));
            await Assert.ThrowsAsync<DlmsProtocolException>(async () => await reading);
            Assert.Equal(ClientSessionState.Faulted, client.State);
        }
    }

    [Theory]
    [InlineData("060000")]
    [InlineData("0000")]
    public async Task RejectsTruncatedOrTrailingAxdrInCompletedTransfer(string hex)
    {
        var pair = DuplexApduChannel.CreatePair();
        await using var peer = new SessionPeer(pair.Server);
        await using var client = new DlmsClientSession(pair.Client, SessionPeer.Options());
        await peer.AssociateAsync(client);
        var reading = SessionPeer.GetAsync(client, TestContext.Current.CancellationToken).AsTask();
        var request = await peer.ReadAsync<LnRequest>();
        await peer.SendAsync(new GetBlockResponse(request.InvokeIdAndPriority, true, 1, FromHex(hex).ToImmutableArray()));
        await Assert.ThrowsAsync<DlmsProtocolException>(async () => await reading);
    }

    [Fact]
    public async Task ReturnsRemoteBlockErrorWithoutFaultingTheAssociation()
    {
        var pair = DuplexApduChannel.CreatePair();
        await using var peer = new SessionPeer(pair.Server);
        await using var client = new DlmsClientSession(pair.Client, SessionPeer.Options());
        await peer.AssociateAsync(client);
        var reading = SessionPeer.GetAsync(client, TestContext.Current.CancellationToken).AsTask();
        var request = await peer.ReadAsync<LnRequest>();
        await peer.SendAsync(new GetBlockResponse(request.InvokeIdAndPriority, true, 1, [], CosemResultCode.LongGetAborted));
        Assert.Equal(CosemResultCode.LongGetAborted, (await Assert.ThrowsAsync<DlmsOperationException>(async () => await reading)).Error);
        Assert.Equal(ClientSessionState.Associated, client.State);
        await peer.ReleaseAsync(client);
    }
}
