using EMCL.Client.Association;
using EMCL.Client.Exceptions;
using EMCL.Client.Sessions;
using EMCL.Core.Codecs;
using EMCL.Core.Objects;
using EMCL.Core.Operations;
using EMCL.Core.Values;
using EMCL.Protocol.Apdu;
using EMCL.Protocol.Axdr;
using EMCL.Tests.Support;
using EMCL.Transport.Channels;

namespace EMCL.Tests.Client;

public sealed class ClientSetBlockTransferTests
{
    private static readonly DlmsConformance Services = new LnAssociationOptions().Conformance;

    [Theory]
    [InlineData(49, false, null)]
    [InlineData(50, false, null)]
    [InlineData(49, true, null)]
    [InlineData(50, true, null)]
    [InlineData(49, false, (byte)0xC1)]
    [InlineData(50, false, (byte)0xC1)]
    public async Task UsesNormalSetAtTheExactPduBoundaryAndBlocksAboveIt(int size, bool selection, byte? mismatchedResponseInvoke)
    {
        var pair = DuplexApduChannel.CreatePair();
        var channel = new RecordingChannel(pair.Client);
        await using var peer = new SessionPeer(pair.Server);
        await using var client = new DlmsClientSession(channel, SessionPeer.Options() with
        {
            IgnoreInvokeIdValidation = mismatchedResponseInvoke.HasValue
        });
        var access = selection ? new SelectiveAccess(1, DlmsValue.Octets([1, 2, 3])) : null;
        // Selective access contributes one selector and five parameter octets to the descriptor.
        ushort pdu = (ushort)(selection ? 70 : 64);
        await peer.AssociateAsync(client, serverPdu: pdu);
        var value = DlmsValue.Octets(new byte[size]);
        var writing = SetAsync(client, value, access);
        if (size == 49)
        {
            var request = await peer.ReadAsync<LnRequest>();
            Assert.Equal(CosemOperationKind.Write, request.Operation.Kind);
            Assert.Equal(pdu, channel.Sent[^1].Length);
            await peer.SendAsync(new LnResponse(CosemOperationKind.Write, mismatchedResponseInvoke ?? request.InvokeIdAndPriority,
                OperationResult.Ok()));
        }
        else
        {
            await CompleteBlocksAsync(peer, writing, value, access, mismatchedResponseInvoke);
        }

        await writing;
        Assert.All(channel.Sent.Skip(1), bytes => Assert.True(bytes.Length <= pdu));
        await peer.ReleaseAsync(client);
    }

    [Theory]
    [InlineData(146, false)]
    [InlineData(147, false)]
    [InlineData(148, true)]
    [InlineData(275, false)]
    [InlineData(276, false)]
    [InlineData(277, false)]
    public async Task FitsEveryBlockIncludingLongAxdrLengthDeterminants(ushort pdu, bool wrapper)
    {
        var memory = DuplexApduChannel.CreatePair();
        var pair = wrapper ? DuplexByteTransport.CreateWrapperPair() : (Client: (IApduChannel)memory.Client, Server: (IApduChannel)memory.Server);
        var channel = new RecordingChannel(pair.Client);
        await using var peer = new SessionPeer(pair.Server);
        await using var client = new DlmsClientSession(channel, SessionPeer.Options());
        await peer.AssociateAsync(client, serverPdu: pdu);
        var value = DlmsValue.Octets(Enumerable.Range(0, 1000).Select(i => (byte)i).ToArray());
        var writing = SetAsync(client, value);
        await CompleteBlocksAsync(peer, writing, value);
        Assert.All(channel.Sent.Skip(1), bytes => Assert.InRange(bytes.Length, 1, pdu));
        await peer.ReleaseAsync(client);
    }

    [Fact]
    public async Task DoesNotUseBlocksUnlessBothProposedAndAccepted()
    {
        foreach (bool proposed in new[] { false, true })
        {
            var pair = DuplexApduChannel.CreatePair();
            var channel = new RecordingChannel(pair.Client);
            await using var peer = new SessionPeer(pair.Server);
            await using var client = new DlmsClientSession(channel, SessionPeer.Options() with
            {
                Association = new()
                {
                    Conformance = proposed ? Services : Services & ~DlmsConformance.BlockTransferWithSet,
                    AdvertisedConformance = Services,
                    AcceptUnexpectedBlockTransferWithSet = true
                }
            });
            await peer.AssociateAsync(client, proposed ? Services & ~DlmsConformance.BlockTransferWithSet : Services, 64);
            await Assert.ThrowsAsync<NotSupportedException>(() => SetAsync(client, DlmsValue.Octets(new byte[50])));
            Assert.Single(channel.Sent);
            Assert.Equal(ClientSessionState.Associated, client.State);
            await peer.ReleaseAsync(client);
        }
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(2, false)]
    [InlineData(3, false)]
    [InlineData(1, true)]
    [InlineData(2, true)]
    [InlineData(3, true)]
    public async Task PreservesEarlyAndFinalMeterErrorsAndAllowsTheNextOperation(int rejectAt, bool normalError)
    {
        var pair = DuplexApduChannel.CreatePair();
        var channel = new RecordingChannel(pair.Client);
        await using var peer = new SessionPeer(pair.Server);
        await using var client = new DlmsClientSession(channel, SessionPeer.Options());
        await peer.AssociateAsync(client, serverPdu: 64);
        var writing = SetAsync(client, DlmsValue.Octets(new byte[140]));
        var first = await peer.ReadAsync<SetFirstBlockRequest>();
        uint number = first.BlockNumber;
        bool last = first.LastBlock;
        while (number < rejectAt)
        {
            await peer.SendAsync(new SetBlockResponse(first.InvokeIdAndPriority, number));
            var next = await peer.ReadAsync<SetBlockRequest>();
            number = next.BlockNumber;
            last = next.LastBlock;
        }

        Assert.Equal(rejectAt == 3, last);
        await peer.SendAsync(normalError
            ? new LnResponse(CosemOperationKind.Write, first.InvokeIdAndPriority, new(CosemResultCode.ReadWriteDenied))
            : new SetBlockResponse(first.InvokeIdAndPriority, number, CosemResultCode.ReadWriteDenied));
        var error = await Assert.ThrowsAsync<DlmsOperationException>(() => writing);
        Assert.Equal(CosemResultCode.ReadWriteDenied, error.Error);
        Assert.Equal(ClientSessionState.Associated, client.State);
        Assert.Equal(rejectAt + 1, channel.Sent.Count);
        await peer.ReleaseAsync(client);
    }

    [Theory]
    [InlineData("wrong-number")]
    [InlineData("wrong-id")]
    [InlineData("wrong-priority")]
    [InlineData("early-success")]
    [InlineData("normal-success")]
    [InlineData("last-ack")]
    [InlineData("last-number")]
    [InlineData("duplicate")]
    public async Task InvalidAcknowledgementsLeaveTheWriteUnconfirmed(string scenario)
    {
        var pair = DuplexApduChannel.CreatePair();
        var channel = new RecordingChannel(pair.Client);
        await using var peer = new SessionPeer(pair.Server);
        await using var client = new DlmsClientSession(channel, SessionPeer.Options());
        await peer.AssociateAsync(client, serverPdu: 64);
        var writing = SetAsync(client, DlmsValue.Octets(new byte[50]));
        var first = await peer.ReadAsync<SetFirstBlockRequest>();
        byte invoke = first.InvokeIdAndPriority;
        if (scenario is "last-ack" or "last-number" or "duplicate")
        {
            await peer.SendAsync(new SetBlockResponse(invoke, 1));
            Assert.True((await peer.ReadAsync<SetBlockRequest>()).LastBlock);
            await peer.SendAsync(new SetBlockResponse(invoke, scenario == "last-number" ? 3u : scenario == "duplicate" ? 1u : 2u,
                scenario == "last-number" ? CosemResultCode.Success : null));
        }
        else
        {
            await peer.SendAsync(scenario == "normal-success"
                ? new LnResponse(CosemOperationKind.Write, invoke, OperationResult.Ok())
                : new SetBlockResponse(scenario == "wrong-id" ? (byte)(invoke + 1)
                    : scenario == "wrong-priority" ? (byte)(invoke ^ InvokeIdEncoding.HighPriority) : invoke,
                    scenario == "wrong-number" ? 2u : 1u, scenario == "early-success" ? CosemResultCode.Success : null));
        }

        var error = await Assert.ThrowsAsync<DlmsWriteUnconfirmedException>(() => writing);
        Assert.IsType<DlmsProtocolException>(error.InnerException);
        Assert.Equal(ClientSessionState.Faulted, client.State);
        Assert.Equal(scenario is "last-ack" or "last-number" or "duplicate" ? 3 : 2, channel.Sent.Count);
    }

    [Theory]
    [InlineData("timeout", false)]
    [InlineData("timeout", true)]
    [InlineData("cancel", false)]
    [InlineData("cancel", true)]
    [InlineData("disconnect", false)]
    [InlineData("disconnect", true)]
    public async Task NeverRetriesAfterCancellationDisconnectOrLostFinalReply(string cause, bool final)
    {
        var pair = DuplexApduChannel.CreatePair();
        var channel = new RecordingChannel(pair.Client);
        await using var peer = new SessionPeer(pair.Server);
        var clock = new ControlledDeadlineProvider();
        await using var client = new DlmsClientSession(channel, SessionPeer.Options() with
        {
            TimeProvider = clock
        });
        await peer.AssociateAsync(client, serverPdu: 64);
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var writing = client.SetAsync(1, SessionPeer.RegisterName, 2, DlmsValue.Octets(new byte[50]), cancellationToken: cancel.Token).AsTask();
        var first = await peer.ReadAsync<SetFirstBlockRequest>();
        if (final)
        {
            await peer.SendAsync(new SetBlockResponse(first.InvokeIdAndPriority, 1));
            Assert.True((await peer.ReadAsync<SetBlockRequest>()).LastBlock);
        }

        if (cause == "timeout")
        {
            clock.ExpireDeadlines();
        }
        if (cause == "cancel")
        {
            await cancel.CancelAsync();
        }
        if (cause == "disconnect")
        {
            await pair.Server.DisposeAsync();
        }
        var error = await Assert.ThrowsAsync<DlmsWriteUnconfirmedException>(() => writing);
        if (cause == "timeout")
        {
            Assert.IsType<TimeoutException>(error.InnerException);
        }
        if (cause == "cancel")
        {
            Assert.IsAssignableFrom<OperationCanceledException>(error.InnerException);
        }
        if (cause == "disconnect")
        {
            Assert.IsType<EndOfStreamException>(error.InnerException);
        }
        Assert.Equal(ClientSessionState.Faulted, client.State);
        Assert.Equal(final ? 3 : 2, channel.Sent.Count);
    }

    [Fact]
    public async Task BoundsTheWholeValueAndBlockCountBeforeAnySend()
    {
        var pair = DuplexApduChannel.CreatePair();
        var channel = new RecordingChannel(pair.Client);
        await using var peer = new SessionPeer(pair.Server);
        await using var client = new DlmsClientSession(channel, SessionPeer.Options() with
        {
            Limits = new()
            {
                MaxMessageBytes = 256,
                MaxBlockCount = 2
            },
            Association = new()
            {
                MaxReceivePduSize = 256
            }
        });
        await peer.AssociateAsync(client, serverPdu: 64);
        await Assert.ThrowsAsync<ArgumentException>(() => SetAsync(client, DlmsValue.Octets(new byte[257])));
        await Assert.ThrowsAsync<ArgumentException>(() => SetAsync(client, DlmsValue.Octets(new byte[140])));
        Assert.Single(channel.Sent);
        Assert.Equal(ClientSessionState.Associated, client.State);
        await peer.ReleaseAsync(client);
    }

    [Fact]
    public async Task AllowsAValueAtTheLocalLimitEvenWhenNormalHeadersWouldExceedIt()
    {
        var pair = DuplexApduChannel.CreatePair();
        var channel = new RecordingChannel(pair.Client);
        await using var peer = new SessionPeer(pair.Server);
        await using var client = new DlmsClientSession(channel, SessionPeer.Options() with
        {
            Limits = new()
            {
                MaxMessageBytes = 256
            },
            Association = new()
            {
                MaxReceivePduSize = 256
            }
        });
        await peer.AssociateAsync(client, serverPdu: 0);
        var value = DlmsValue.Octets(new byte[253]);
        Assert.Equal(256, new AxdrCodec().EncodeBytes(value).Length);
        var writing = SetAsync(client, value);
        await CompleteBlocksAsync(peer, writing, value);
        Assert.All(channel.Sent.Skip(1), bytes => Assert.True(bytes.Length <= 256));
        await peer.ReleaseAsync(client);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReportsUnconfirmedOutcomeWhenANormalOrBlockSendFails(bool blocks)
    {
        var pair = DuplexApduChannel.CreatePair();
        var channel = new RecordingChannel(pair.Client);
        await using var peer = new SessionPeer(pair.Server);
        await using var client = new DlmsClientSession(channel, SessionPeer.Options());
        await peer.AssociateAsync(client, serverPdu: 64);
        channel.FailSetSend = true;
        var error = await Assert.ThrowsAsync<DlmsWriteUnconfirmedException>(() => SetAsync(client,
            DlmsValue.Octets(new byte[blocks ? 50 : 1])));
        Assert.IsType<IOException>(error.InnerException);
        Assert.Equal(2, channel.Sent.Count);
        Assert.Equal(ClientSessionState.Faulted, client.State);
    }

    [Fact]
    public async Task ReportsALostNormalSetReplyAsUnconfirmedWithoutRetry()
    {
        var pair = DuplexApduChannel.CreatePair();
        var channel = new RecordingChannel(pair.Client);
        await using var peer = new SessionPeer(pair.Server);
        var clock = new ControlledDeadlineProvider();
        await using var client = new DlmsClientSession(channel, SessionPeer.Options() with
        {
            TimeProvider = clock
        });
        await peer.AssociateAsync(client);
        var writing = SetAsync(client, DlmsValue.Octets([1]));
        await peer.ReadAsync<LnRequest>();
        clock.ExpireDeadlines();
        var error = await Assert.ThrowsAsync<DlmsWriteUnconfirmedException>(() => writing);
        Assert.IsType<TimeoutException>(error.InnerException);
        Assert.Equal(2, channel.Sent.Count);
        Assert.Equal(ClientSessionState.Faulted, client.State);
    }

    [Fact]
    public async Task KeepsConcurrentSetTransfersAndTheirFinalResultsSeparate()
    {
        var pair = DuplexApduChannel.CreatePair();
        await using var peer = new SessionPeer(pair.Server);
        await using var client = new DlmsClientSession(pair.Client, SessionPeer.Options(concurrency: 2));
        await peer.AssociateAsync(client, serverPdu: 64);
        var first = SetAsync(client, DlmsValue.Octets(new byte[50]));
        var second = SetAsync(client, DlmsValue.Octets(new byte[51]));
        var request1 = await peer.ReadAsync<SetFirstBlockRequest>();
        var request2 = await peer.ReadAsync<SetFirstBlockRequest>();
        Assert.NotEqual(request1.InvokeIdAndPriority, request2.InvokeIdAndPriority);
        await peer.SendAsync(new SetBlockResponse(request2.InvokeIdAndPriority, 1));
        Assert.Equal(request2.InvokeIdAndPriority, (await peer.ReadAsync<SetBlockRequest>()).InvokeIdAndPriority);
        await peer.SendAsync(new SetBlockResponse(request1.InvokeIdAndPriority, 1));
        Assert.Equal(request1.InvokeIdAndPriority, (await peer.ReadAsync<SetBlockRequest>()).InvokeIdAndPriority);
        await peer.SendAsync(new SetBlockResponse(request2.InvokeIdAndPriority, 2, CosemResultCode.ReadWriteDenied));
        await peer.SendAsync(new SetBlockResponse(request1.InvokeIdAndPriority, 2, CosemResultCode.Success));
        await first;
        Assert.Equal(CosemResultCode.ReadWriteDenied, (await Assert.ThrowsAsync<DlmsOperationException>(() => second)).Error);
        await peer.ReleaseAsync(client);
    }

    [Fact]
    public async Task CancellationWhileQueuedDoesNotSendOrChangeTheWriteOutcome()
    {
        var pair = DuplexApduChannel.CreatePair();
        var channel = new RecordingChannel(pair.Client);
        await using var peer = new SessionPeer(pair.Server);
        await using var client = new DlmsClientSession(channel, SessionPeer.Options());
        await peer.AssociateAsync(client, serverPdu: 64);
        var writing = SetAsync(client, DlmsValue.Octets(new byte[50]));
        var first = await peer.ReadAsync<SetFirstBlockRequest>();
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var queued = client.SetAsync(1, SessionPeer.RegisterName, 2, DlmsValue.Null, cancellationToken: cancel.Token).AsTask();
        await cancel.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => queued);
        Assert.Equal(2, channel.Sent.Count);
        Assert.Equal(ClientSessionState.Associated, client.State);
        await peer.SendAsync(new SetBlockResponse(first.InvokeIdAndPriority, 1));
        await peer.ReadAsync<SetBlockRequest>();
        await peer.SendAsync(new SetBlockResponse(first.InvokeIdAndPriority, 2, CosemResultCode.Success));
        await writing;
        await peer.ReleaseAsync(client);
    }

    private static Task SetAsync(DlmsClientSession client, DlmsValue value, SelectiveAccess? selection = null) =>
        client.SetAsync(1, SessionPeer.RegisterName, 2, value, selection, TestContext.Current.CancellationToken).AsTask();

    private static async Task CompleteBlocksAsync(SessionPeer peer, Task writing, DlmsValue value,
        SelectiveAccess? selection = null, byte? mismatchedResponseInvoke = null)
    {
        var first = await peer.ReadAsync<SetFirstBlockRequest>();
        Assert.Equal(1u, first.BlockNumber);
        Assert.Equal((ushort)1, first.Operation.ClassId);
        Assert.Equal(SessionPeer.RegisterName, first.Operation.LogicalName);
        Assert.Equal((sbyte)2, first.Operation.Index);
        Assert.Null(first.Operation.Value);
        if (selection != null)
        {
            Assert.Equal(selection.Selector, first.Operation.Selection!.Selector);
            AssertBytesEqual(new AxdrCodec().EncodeBytes(selection.Parameters), new AxdrCodec().EncodeBytes(first.Operation.Selection.Parameters));
        }
        else
        {
            Assert.Null(first.Operation.Selection);
        }

        var bytes = first.Data.ToList();
        bool last = first.LastBlock;
        uint number = 1;
        byte responseInvoke = mismatchedResponseInvoke ?? first.InvokeIdAndPriority;
        while (!last)
        {
            Assert.False(writing.IsCompleted);
            await peer.SendAsync(new SetBlockResponse(responseInvoke, number));
            var next = await peer.ReadAsync<SetBlockRequest>();
            Assert.Equal(++number, next.BlockNumber);
            Assert.Equal(first.InvokeIdAndPriority, next.InvokeIdAndPriority);
            Assert.NotEmpty(next.Data);
            bytes.AddRange(next.Data);
            last = next.LastBlock;
        }

        Assert.False(writing.IsCompleted);
        AssertBytesEqual(new AxdrCodec().EncodeBytes(value), bytes.ToArray());
        await peer.SendAsync(new SetBlockResponse(responseInvoke, number, CosemResultCode.Success));
        await writing;
    }

    private sealed class RecordingChannel(IApduChannel inner) : IApduChannel
    {
        internal List<byte[]> Sent { get; } = [];
        internal bool FailSetSend
        {
            get; set;
        }
        public ValueTask SendAsync(ReadOnlyMemory<byte> apdu, CancellationToken cancellationToken = default)
        {
            Sent.Add(apdu.ToArray());
            if (FailSetSend && apdu.Span[0] == (byte)ApduTag.SetRequest)
            {
                throw new IOException("The channel failed during a SET send attempt.");
            }

            return inner.SendAsync(apdu, cancellationToken);
        }
        public ValueTask<ReadOnlyMemory<byte>?> ReceiveAsync(CancellationToken cancellationToken = default) => inner.ReceiveAsync(cancellationToken);
        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }
}
