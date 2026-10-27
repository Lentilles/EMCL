using EMCL.Client.Exceptions;
using EMCL.Client.Sessions;
using EMCL.Core.Codecs;
using EMCL.Core.Operations;
using EMCL.Core.Values;
using EMCL.Protocol.Acse;
using EMCL.Protocol.Apdu;
using EMCL.Tests.Support;

namespace EMCL.Tests.Client;

public sealed class ClientLifecycleTests
{
    [Fact]
    public async Task TimerCreationFailureDoesNotStartOrStrandAnAssociation()
    {
        var pair = DuplexApduChannel.CreatePair();
        await using var peer = new SessionPeer(pair.Server);
        await using var client = new DlmsClientSession(pair.Client, SessionPeer.Options() with
        {
            TimeProvider = new FailingTimerProvider()
        });
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await client.AssociateAsync(TestContext.Current.CancellationToken));
        Assert.Equal(ClientSessionState.Created, client.State);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellationOrDisposalDuringAssociationStopsTheControlExchange(bool dispose)
    {
        var pair = DuplexApduChannel.CreatePair();
        await using var peer = new SessionPeer(pair.Server);
        await using var client = new DlmsClientSession(pair.Client, SessionPeer.Options());
        using var canceled = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var associating = client.AssociateAsync(canceled.Token).AsTask();
        await peer.ReadAsync<AcseApdu>();
        if (dispose)
        {
            await client.DisposeAsync();
            await Assert.ThrowsAsync<ObjectDisposedException>(async () => await associating);
        }
        else
        {
            await canceled.CancelAsync();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await associating);
            Assert.Equal(ClientSessionState.Faulted, client.State);
        }
    }

    [Fact]
    public async Task RejectsNonNormalReleaseAndFaultsTheSession()
    {
        var pair = DuplexApduChannel.CreatePair();
        await using var peer = new SessionPeer(pair.Server);
        await using var client = new DlmsClientSession(pair.Client, SessionPeer.Options());
        await peer.AssociateAsync(client);
        var releasing = client.ReleaseAsync(TestContext.Current.CancellationToken).AsTask();
        await peer.ReadAsync<AcseApdu>();
        await peer.SendAsync(AcseApdu.Release(response: true, reason: 1));
        await Assert.ThrowsAsync<DlmsAssociationException>(async () => await releasing);
        Assert.Equal(ClientSessionState.Faulted, client.State);
    }

    [Fact]
    public async Task CancellationBeforeSendLeavesAssociationUsableAndCancellationAfterSendFaultsIt()
    {
        var pair = DuplexApduChannel.CreatePair();
        await using var peer = new SessionPeer(pair.Server);
        await using var client = new DlmsClientSession(pair.Client, SessionPeer.Options());
        await peer.AssociateAsync(client);
        using var canceled = new CancellationTokenSource();
        await canceled.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await SessionPeer.GetAsync(client, canceled.Token));
        Assert.Equal(ClientSessionState.Associated, client.State);
        using var active = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var reading = SessionPeer.GetAsync(client, active.Token).AsTask();
        await peer.ReadAsync<LnRequest>();
        await active.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await reading);
        Assert.Equal(ClientSessionState.Faulted, client.State);
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await SessionPeer.GetAsync(client, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CancelingAQueuedOperationDoesNotAffectTheOutstandingExchange()
    {
        var pair = DuplexApduChannel.CreatePair();
        await using var peer = new SessionPeer(pair.Server);
        await using var client = new DlmsClientSession(pair.Client, SessionPeer.Options());
        await peer.AssociateAsync(client);
        var first = SessionPeer.GetAsync(client, TestContext.Current.CancellationToken).AsTask();
        var request = await peer.ReadAsync<LnRequest>();
        using var canceled = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var queued = SessionPeer.GetAsync(client, canceled.Token).AsTask();
        await canceled.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await queued);
        await peer.SendAsync(new LnResponse(CosemOperationKind.Read, request.InvokeIdAndPriority, OperationResult.Ok(DlmsValue.Null)));
        Assert.Equal(DlmsType.Null, (await first).Type);
        Assert.Equal(ClientSessionState.Associated, client.State);
    }

    [Fact]
    public async Task ExpiresAnEntireExchangeAndStopsLateResponseReuse()
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
        clock.ExpireDeadlines();
        await Assert.ThrowsAsync<TimeoutException>(async () => await reading);
        await peer.SendAsync(new LnResponse(CosemOperationKind.Read, request.InvokeIdAndPriority, OperationResult.Ok(DlmsValue.Null)));
        Assert.Equal(ClientSessionState.Faulted, client.State);
        Assert.IsType<TimeoutException>(client.Failure);
    }

    [Fact]
    public async Task DrainsOutstandingOperationsBeforeReleaseAndRejectsNewRequests()
    {
        var pair = DuplexApduChannel.CreatePair();
        await using var peer = new SessionPeer(pair.Server);
        await using var client = new DlmsClientSession(pair.Client, SessionPeer.Options());
        await peer.AssociateAsync(client);
        var reading = SessionPeer.GetAsync(client, TestContext.Current.CancellationToken).AsTask();
        var request = await peer.ReadAsync<LnRequest>();
        var releasing = client.ReleaseAsync(TestContext.Current.CancellationToken).AsTask();
        Assert.Equal(ClientSessionState.Releasing, client.State);
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await SessionPeer.GetAsync(client, TestContext.Current.CancellationToken));
        await peer.SendAsync(new LnResponse(CosemOperationKind.Read, request.InvokeIdAndPriority, OperationResult.Ok(DlmsValue.Null)));
        await reading;
        Assert.Equal(AcseKind.Rlrq, (await peer.ReadAsync<AcseApdu>()).Kind);
        await peer.SendAsync(AcseApdu.Release(response: true));
        await releasing;
        Assert.Equal(ClientSessionState.Released, client.State);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DisposalCancelsPendingIoAndHonorsChannelOwnership(bool leaveOpen)
    {
        var pair = DuplexApduChannel.CreatePair();
        await using var peer = new SessionPeer(pair.Server);
        var client = new DlmsClientSession(pair.Client, SessionPeer.Options() with
        {
            LeaveOpen = leaveOpen
        });
        await peer.AssociateAsync(client);
        var reading = SessionPeer.GetAsync(client, TestContext.Current.CancellationToken).AsTask();
        await peer.ReadAsync<LnRequest>();
        var queued = SessionPeer.GetAsync(client, TestContext.Current.CancellationToken).AsTask();
        await client.DisposeAsync();
        await client.DisposeAsync();
        await Assert.ThrowsAsync<ObjectDisposedException>(async () => await reading);
        await Assert.ThrowsAsync<ObjectDisposedException>(async () => await queued);
        Assert.Equal(ClientSessionState.Disposed, client.State);
        Assert.Equal(leaveOpen ? 0 : 1, pair.Client.DisposeCount);
        await Assert.ThrowsAsync<ObjectDisposedException>(async () => await SessionPeer.GetAsync(client, TestContext.Current.CancellationToken));
        if (leaveOpen)
        {
            await pair.Client.DisposeAsync();
        }
    }

    [Theory]
    [InlineData("C401410000")]
    [InlineData("C5014000")]
    [InlineData("FF")]
    [InlineData("C40140000000")]
    public async Task FaultsOnWrongInvokeServiceOrMalformedApdu(string hex)
    {
        var pair = DuplexApduChannel.CreatePair();
        await using var peer = new SessionPeer(pair.Server);
        await using var client = new DlmsClientSession(pair.Client, SessionPeer.Options());
        await peer.AssociateAsync(client);
        var reading = SessionPeer.GetAsync(client, TestContext.Current.CancellationToken).AsTask();
        await peer.ReadAsync<LnRequest>();
        await peer.SendRawAsync(FromHex(hex));
        await Assert.ThrowsAsync<DlmsProtocolException>(async () => await reading);
        Assert.Equal(ClientSessionState.Faulted, client.State);
    }

    [Fact]
    public async Task FaultsOnEofAndNotificationOverflowWithoutBlockingRequests()
    {
        foreach (bool eof in new[] { false, true })
        {
            var pair = DuplexApduChannel.CreatePair();
            await using var peer = new SessionPeer(pair.Server);
            await using var client = new DlmsClientSession(pair.Client, SessionPeer.Options() with
            {
                NotificationCapacity = 1
            });
            await peer.AssociateAsync(client);
            var reading = SessionPeer.GetAsync(client, TestContext.Current.CancellationToken).AsTask();
            await peer.ReadAsync<LnRequest>();
            if (eof)
            {
                await pair.Server.DisposeAsync();
                await Assert.ThrowsAsync<EndOfStreamException>(async () => await reading);
            }
            else
            {
                await peer.SendAsync(new DataNotification(1, [], DlmsValue.Null));
                await peer.SendAsync(new DataNotification(2, [], DlmsValue.Null));
                await Assert.ThrowsAsync<DlmsProtocolException>(async () => await reading);
            }

            Assert.Equal(ClientSessionState.Faulted, client.State);
        }
    }

    private sealed class FailingTimerProvider : TimeProvider
    {
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) =>
            throw new InvalidOperationException("The injected timer source failed.");
    }
}
