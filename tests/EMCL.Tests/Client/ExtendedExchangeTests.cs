using System.Buffers;
using System.Collections.Immutable;
using EMCL.Client.Association;
using EMCL.Client.Exceptions;
using EMCL.Client.Sessions;
using EMCL.Core.Codecs;
using EMCL.Core.Objects;
using EMCL.Core.Operations;
using EMCL.Core.Values;
using EMCL.Messaging;
using EMCL.Protocol.Apdu;
using EMCL.Protocol.Axdr;
using EMCL.Tests.Support;

namespace EMCL.Tests.Client;

public sealed class ExtendedExchangeTests
{
    private static readonly ApduCodec Codec = new();

    [Fact]
    public async Task ActionCanTransferBlocksInBothDirections()
    {
        var pair = DuplexByteTransport.CreateWrapperPair();
        await using var peer = new SessionPeer(pair.Server);
        var services = DlmsConformance.Action | DlmsConformance.BlockTransferWithAction;
        await using var client = new DlmsClientSession(pair.Client, SessionPeer.Options() with
        {
            Association = new LnAssociationOptions { Conformance = services }
        });
        await peer.AssociateAsync(client, services, serverPdu: 64);
        var ct = TestContext.Current.CancellationToken;
        var parameter = DlmsValue.Octets(Enumerable.Range(0, 160).Select(value => (byte)value).ToArray());
        var action = client.ActionAsync(18, SessionPeer.RegisterName, 2, parameter, ct).AsTask();
        var bytes = new ArrayBufferWriter<byte>();
        var first = await peer.ReadAsync<ActionBlock>();
        Assert.NotNull(first.Method);
        Assert.Equal(1u, first.BlockNumber);
        var block = first;
        while (true)
        {
            Assert.True(Codec.EncodeBytes(block).Length <= 64);
            bytes.Write(block.Data.AsSpan());
            if (block.LastBlock)
            {
                break;
            }

            await peer.SendAsync(new ActionNextBlock(block.InvokeIdAndPriority, true, block.BlockNumber));
            uint nextNumber = block.BlockNumber + 1;
            block = await peer.ReadAsync<ActionBlock>();
            Assert.Null(block.Method);
            Assert.Equal(nextNumber, block.BlockNumber);
        }

        Assert.Equal(new AxdrCodec().EncodeBytes(parameter), bytes.WrittenSpan.ToArray());
        byte invoke = block.InvokeIdAndPriority;
        byte[] result = Codec.EncodeBytes(new LnResponse(CosemOperationKind.Invoke, invoke,
            OperationResult.Ok(DlmsValue.Octets([1, 2, 3]))))[3..];
        await peer.SendAsync(new ActionBlock(invoke, true, false, 1, [.. result.AsSpan(0, 2)]));
        Assert.Equal(new ActionNextBlock(invoke, false, 1), await peer.ReadAsync<ActionNextBlock>());
        await peer.SendAsync(new ActionBlock(invoke, true, true, 2, [.. result.AsSpan(2)]));
        Assert.Equal(new byte[] { 1, 2, 3 }, (await action)!.Bytes.ToArray());
        await peer.ReleaseAsync(client);
    }

    [Fact]
    public async Task ReportsAnUnconfirmedActionAfterDisconnect()
    {
        var pair = DuplexApduChannel.CreatePair();
        var peer = new SessionPeer(pair.Server);
        await using var client = new DlmsClientSession(pair.Client, SessionPeer.Options());
        await peer.AssociateAsync(client);
        var action = client.ActionAsync(18, SessionPeer.RegisterName, 2,
            cancellationToken: TestContext.Current.CancellationToken).AsTask();
        await peer.ReadAsync<LnRequest>();
        await peer.DisposeAsync();
        await Assert.ThrowsAsync<DlmsActionUnconfirmedException>(async () => await action);
    }

    [Fact]
    public async Task ReceivesAndConfirmsANotificationAcrossGeneralBlocks()
    {
        var pair = DuplexApduChannel.CreatePair();
        await using var peer = new SessionPeer(pair.Server);
        var services = DlmsConformance.DataNotification | DlmsConformance.GeneralBlockTransfer;
        await using var client = new DlmsClientSession(pair.Client, SessionPeer.Options() with
        {
            Association = new LnAssociationOptions { Conformance = services }
        });
        await peer.AssociateAsync(client, services);
        var ct = TestContext.Current.CancellationToken;
        await using var reader = client.ReadNotificationsAsync(ct).GetAsyncEnumerator(ct);
        uint invoke = DataNotification.ConfirmedService | 0x19;
        byte[] bytes = Codec.EncodeBytes(new DataNotification(invoke, [], DlmsValue.Array(DlmsValue.Null)));
        await peer.SendAsync(new GeneralBlockTransfer(false, false, 1, 1, 0, [.. bytes.AsSpan(0, 4)]));
        var ack = await peer.ReadAsync<GeneralBlockTransfer>();
        Assert.Equal((ushort)1, ack.BlockNumberAcknowledged);
        Assert.Empty(ack.Data);
        await peer.SendAsync(new GeneralBlockTransfer(false, false, 1, 3, ack.BlockNumber, [0]));
        Assert.Equal(ack, await peer.ReadAsync<GeneralBlockTransfer>());
        await peer.SendAsync(new GeneralBlockTransfer(false, false, 1, 1, 0, [.. bytes.AsSpan(0, 4)]));
        Assert.Equal(ack, await peer.ReadAsync<GeneralBlockTransfer>());
        await peer.SendAsync(new GeneralBlockTransfer(true, false, 1, 2, ack.BlockNumber, [.. bytes.AsSpan(4)]));
        Assert.True(await reader.MoveNextAsync());
        await client.ConfirmNotificationAsync(reader.Current, ct);
        var confirmed = await peer.ReadAsync<GeneralBlockTransfer>();
        Assert.True(confirmed.LastBlock);
        Assert.Equal((ushort)2, confirmed.BlockNumberAcknowledged);
        Assert.Equal(Convert.FromHexString("104000001900"), confirmed.Data.ToArray());
        await peer.ReleaseAsync(client);
    }

    [Fact]
    public async Task OversizedActionUsesGeneralBlocksWhenServiceSpecificBlocksAreUnavailable()
    {
        var pair = DuplexByteTransport.CreateWrapperPair();
        await using var peer = new SessionPeer(pair.Server);
        var services = DlmsConformance.Action | DlmsConformance.GeneralBlockTransfer;
        await using var client = new DlmsClientSession(pair.Client, SessionPeer.Options() with
        {
            Association = new LnAssociationOptions { Conformance = services }
        });
        await peer.AssociateAsync(client, services, serverPdu: 64);
        var ct = TestContext.Current.CancellationToken;
        var parameter = DlmsValue.Octets(Enumerable.Range(0, 160).Select(value => (byte)value).ToArray());
        var action = client.ActionAsync(18, SessionPeer.RegisterName, 2, parameter, ct).AsTask();
        var encoded = new ArrayBufferWriter<byte>();
        ushort number = 0;
        while (true)
        {
            var block = await peer.ReadAsync<GeneralBlockTransfer>();
            Assert.Equal(number, block.BlockNumberAcknowledged);
            Assert.Equal(++number, block.BlockNumber);
            Assert.True(Codec.EncodeBytes(block).Length <= 64);
            encoded.Write(block.Data.AsSpan());
            if (block.LastBlock)
            {
                break;
            }

            await peer.SendAsync(new GeneralBlockTransfer(false, false, 1, number, number, []));
        }

        var request = Assert.IsType<LnRequest>(Codec.Decode(new ReadOnlySequence<byte>(encoded.WrittenMemory)).Value);
        Assert.Equal(parameter.Bytes.ToArray(), request.Operation.Value!.Bytes.ToArray());
        var result = new LnResponse(CosemOperationKind.Invoke, request.InvokeIdAndPriority, OperationResult.Ok());
        await peer.SendAsync(new GeneralBlockTransfer(true, false, 1, number, number, [.. Codec.EncodeBytes(result)]));
        Assert.Null(await action);
        await peer.ReleaseAsync(client);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TransfersPreEstablishedPushAcrossWrapperBlocks(bool confirmed)
    {
        var pair = DuplexByteTransport.CreateWrapperPair();
        await using var source = pair.Server;
        await using var destination = pair.Client;
        var ct = TestContext.Current.CancellationToken;
        var sender = new SpodesPushChannel(source, TimeSpan.FromSeconds(2), maximumPduSize: 48);
        var receiver = new SpodesPushReceiver(destination);
        uint invoke = 0x19u | (confirmed ? DataNotification.ConfirmedService : 0u);
        var now = CosemDateTime.FromDateTimeOffset(DateTimeOffset.UtcNow).Octets;
        var data = Enumerable.Range(0, 120).Select(value => (byte)value).ToArray();
        var notification = new DataNotification(invoke, now, DlmsValue.Octets(data));
        var pending = sender.SendAsync(LogicalName.Parse("0.0.25.9.0.255"), DlmsValue.Null, notification, ct).AsTask();
        Assert.True(await receiver.ReceiveAsync((received, _) =>
        {
            Assert.Equal(invoke, received.LongInvokeIdAndPriority);
            Assert.Equal(data, received.Body.Bytes.ToArray());
            return ValueTask.FromResult(true);
        }, ct));
        Assert.True(await pending);
    }
}
