using EMCL.Client.Sessions;
using EMCL.Client.Association;
using EMCL.Composition;
using EMCL.Core.Codecs;
using EMCL.Core.Objects;
using EMCL.Core.Operations;
using EMCL.Core.Values;
using EMCL.Objects.Association;
using EMCL.Protocol.Apdu;
using EMCL.Protocol.Axdr;
using EMCL.Tests.Support;

namespace EMCL.Tests.Client;

public sealed class AssociationObjectStreamTests
{
    [Fact]
    public async Task YieldsFirstObjectBeforeReceivingTheFinalBlock()
    {
        var pair = DuplexApduChannel.CreatePair();
        await using var peer = new SessionPeer(pair.Server);
        await using var client = new DlmsClientSession(pair.Client, SessionPeer.Options());
        await peer.AssociateAsync(client);

        var codec = new AxdrCodec();
        var first = Entry(3, 0, "1.0.1.8.0.255");
        var second = Entry(9000, 5, "0.0.96.1.0.255");
        byte[] bytes = codec.EncodeBytes(DlmsValue.Array(first, second));
        int split = 2 + codec.EncodeBytes(first).Length + 3;
        var opening = client.OpenObjectListAsync(new EmclBuilder().CreateImporter(), AssociationLnMembers.Version1,
            cancellationToken: TestContext.Current.CancellationToken).AsTask();
        var request = await peer.ReadAsync<LnRequest>();
        Assert.Equal(CosemClassIds.AssociationLn, request.Operation.ClassId);
        await peer.SendAsync(new GetBlockResponse(request.InvokeIdAndPriority, false, 1,
            bytes[..split].ToImmutableArray()));

        await using var stream = await opening;
        Assert.Equal(2, stream.TotalCount);
        Assert.Equal(0, stream.CurrentCount);
        await using var iterator = stream.GetAsyncEnumerator(TestContext.Current.CancellationToken);
        Assert.Throws<InvalidOperationException>(() => stream.GetAsyncEnumerator(TestContext.Current.CancellationToken));
        Assert.True(await iterator.MoveNextAsync());
        Assert.Equal(1, stream.CurrentCount);
        Assert.Equal(LogicalName.Parse("1.0.1.8.0.255"), iterator.Current.LogicalName);

        var next = await peer.ReadAsync<GetNextRequest>();
        Assert.Equal(1u, next.BlockNumber);
        var waitingForSecond = iterator.MoveNextAsync().AsTask();
        Assert.False(waitingForSecond.IsCompleted);
        await peer.SendAsync(new GetBlockResponse(request.InvokeIdAndPriority, true, 2,
            bytes[split..].ToImmutableArray()));
        Assert.True(await waitingForSecond);
        Assert.Equal(2, stream.CurrentCount);
        Assert.Equal(LogicalName.Parse("0.0.96.1.0.255"), iterator.Current.LogicalName);
        Assert.False(await iterator.MoveNextAsync());
        await peer.ReleaseAsync(client);
    }

    [Fact]
    public async Task RejectsTrailingEntriesAfterTheDeclaredCount()
    {
        var pair = DuplexApduChannel.CreatePair();
        await using var peer = new SessionPeer(pair.Server);
        await using var client = new DlmsClientSession(pair.Client, SessionPeer.Options());
        await peer.AssociateAsync(client);

        byte[] bytes = new AxdrCodec().EncodeBytes(DlmsValue.Array(
            Entry(3, 0, "1.0.1.8.0.255"), Entry(9000, 5, "0.0.96.1.0.255")));
        bytes[1] = 1;
        var opening = client.OpenObjectListAsync(new EmclBuilder().CreateImporter(), AssociationLnMembers.Version1,
            cancellationToken: TestContext.Current.CancellationToken).AsTask();
        var request = await peer.ReadAsync<LnRequest>();
        await peer.SendAsync(new GetBlockResponse(request.InvokeIdAndPriority, true, 1, bytes.ToImmutableArray()));

        await using var stream = await opening;
        await using var iterator = stream.GetAsyncEnumerator(TestContext.Current.CancellationToken);
        Assert.True(await iterator.MoveNextAsync());
        await Assert.ThrowsAsync<AssociationImportException>(async () => await iterator.MoveNextAsync());
    }

    [Fact]
    public async Task WaitsForAnArrayHeaderSplitAcrossBlocks()
    {
        var pair = DuplexApduChannel.CreatePair();
        await using var peer = new SessionPeer(pair.Server);
        await using var client = new DlmsClientSession(pair.Client, SessionPeer.Options());
        await peer.AssociateAsync(client);

        var opening = client.OpenObjectListAsync(new EmclBuilder().CreateImporter(), AssociationLnMembers.Version1,
            cancellationToken: TestContext.Current.CancellationToken).AsTask();
        var request = await peer.ReadAsync<LnRequest>();
        await peer.SendAsync(new GetBlockResponse(request.InvokeIdAndPriority, false, 1, [1]));
        Assert.False(opening.IsCompleted);
        await peer.ReadAsync<GetNextRequest>();
        await peer.SendAsync(new GetBlockResponse(request.InvokeIdAndPriority, true, 2, [0]));

        await using var stream = await opening;
        Assert.Equal(0, stream.TotalCount);
        await using var iterator = stream.GetAsyncEnumerator(TestContext.Current.CancellationToken);
        Assert.False(await iterator.MoveNextAsync());
        await peer.ReleaseAsync(client);
    }

    [Fact]
    public async Task ReadsAnUnblockedObjectList()
    {
        var pair = DuplexApduChannel.CreatePair();
        await using var peer = new SessionPeer(pair.Server);
        await using var client = new DlmsClientSession(pair.Client, SessionPeer.Options());
        await peer.AssociateAsync(client);

        var opening = client.OpenObjectListAsync(new EmclBuilder().CreateImporter(), AssociationLnMembers.Version1,
            cancellationToken: TestContext.Current.CancellationToken).AsTask();
        var request = await peer.ReadAsync<LnRequest>();
        await peer.SendAsync(new LnResponse(CosemOperationKind.Read, request.InvokeIdAndPriority,
            OperationResult.Ok(DlmsValue.Array(Entry(3, 0, "1.0.1.8.0.255")))));

        await using var stream = await opening;
        Assert.Equal(1, stream.TotalCount);
        await using var iterator = stream.GetAsyncEnumerator(TestContext.Current.CancellationToken);
        Assert.True(await iterator.MoveNextAsync());
        Assert.Equal(1, stream.CurrentCount);
        Assert.False(await iterator.MoveNextAsync());
        await peer.ReleaseAsync(client);
    }

    [Fact]
    public async Task DisposingAnIncompleteStreamCancelsItsGetExchange()
    {
        var pair = DuplexApduChannel.CreatePair();
        await using var peer = new SessionPeer(pair.Server);
        await using var client = new DlmsClientSession(pair.Client, SessionPeer.Options());
        await peer.AssociateAsync(client);

        var opening = client.OpenObjectListAsync(new EmclBuilder().CreateImporter(), AssociationLnMembers.Version1,
            cancellationToken: TestContext.Current.CancellationToken).AsTask();
        var request = await peer.ReadAsync<LnRequest>();
        await peer.SendAsync(new GetBlockResponse(request.InvokeIdAndPriority, false, 1, [1, 1]));
        await using var stream = await opening;
        await peer.ReadAsync<GetNextRequest>();

        await stream.DisposeAsync();
        Assert.Equal(ClientSessionState.Faulted, client.State);
    }

    [Fact]
    public async Task ReadsMultiByteArrayCount()
    {
        var pair = DuplexApduChannel.CreatePair();
        await using var peer = new SessionPeer(pair.Server);
        await using var client = new DlmsClientSession(pair.Client, SessionPeer.Options() with
        {
            Association = new LnAssociationOptions { MaxReceivePduSize = 10000 }
        });
        await peer.AssociateAsync(client);

        var entries = Enumerable.Range(0, 128)
            .Select(_ => Entry(3, 0, "1.0.1.8.0.255")).ToArray();
        var opening = client.OpenObjectListAsync(new EmclBuilder().CreateImporter(), AssociationLnMembers.Version1,
            cancellationToken: TestContext.Current.CancellationToken).AsTask();
        var request = await peer.ReadAsync<LnRequest>();
        await peer.SendAsync(new LnResponse(CosemOperationKind.Read, request.InvokeIdAndPriority,
            OperationResult.Ok(DlmsValue.Array(entries))));

        await using var stream = await opening;
        Assert.Equal(128, stream.TotalCount);
        await foreach (var _ in stream.WithCancellation(TestContext.Current.CancellationToken))
        {
        }

        Assert.Equal(128, stream.CurrentCount);
        await peer.ReleaseAsync(client);
    }
}
