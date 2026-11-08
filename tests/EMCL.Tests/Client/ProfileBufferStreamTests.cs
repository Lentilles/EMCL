using EMCL.Client.Exceptions;
using EMCL.Client.Sessions;
using EMCL.Core.Codecs;
using EMCL.Core.Objects;
using EMCL.Core.Operations;
using EMCL.Core.Values;
using EMCL.Objects.Definitions;
using EMCL.Objects.ProfileGeneric;
using EMCL.Protocol.Apdu;
using EMCL.Protocol.Axdr;
using EMCL.Tests.Support;

namespace EMCL.Tests.Client;

public sealed class ProfileBufferStreamTests
{
    private static readonly LogicalName ProfileName = LogicalName.Parse("1.0.99.1.0.255");
    private const string Array = "0102020211010901410202110209024243";
    private const string CompactArray = "13020211090701014102024243";

    [Theory]
    [InlineData(Array, 10, 2, null, 7, 15)]
    [InlineData(CompactArray, 10, null, 7, 3, 7)]
    public async Task YieldsRowsAndReportsProgressBeforeTheFinalBlock(string hex, int split, int? totalCount,
        int? totalBytes, int firstRowBytes, int allRowBytes)
    {
        var pair = DuplexApduChannel.CreatePair();
        await using var peer = new SessionPeer(pair.Server);
        await using var client = new DlmsClientSession(pair.Client, SessionPeer.Options());
        await peer.AssociateAsync(client);
        var selection = new SelectiveAccess(ProfileSelectors.Entry, DlmsValue.Structure(
            DlmsValue.Number(DlmsType.UInt32, 1), DlmsValue.Number(DlmsType.UInt32, 2),
            DlmsValue.Number(DlmsType.UInt16, 1), DlmsValue.Number(DlmsType.UInt16, 0)));

        var opening = ((IDlmsClientSession)client).OpenProfileBufferAsync(ProfileName, selection,
            TestContext.Current.CancellationToken).AsTask();
        var request = await peer.ReadAsync<LnRequest>();
        Assert.Equal(CosemClassIds.ProfileGeneric, request.Operation.ClassId);
        Assert.Equal(ProfileName, request.Operation.LogicalName);
        Assert.Equal(ProfileGenericMembers.Attributes.Buffer, request.Operation.Index);
        Assert.Equal(ProfileSelectors.Entry, request.Operation.Selection!.Selector);
        Assert.Equal(selection.Parameters.Items, request.Operation.Selection.Parameters.Items.AsEnumerable());

        byte[] bytes = Convert.FromHexString(hex);
        await peer.SendAsync(new GetBlockResponse(request.InvokeIdAndPriority, false, 1, [bytes[0]]));
        Assert.Equal(1u, (await peer.ReadAsync<GetNextRequest>()).BlockNumber);
        Assert.False(opening.IsCompleted);
        await peer.SendAsync(new GetBlockResponse(request.InvokeIdAndPriority, false, 2, [.. bytes[1..split]]));

        await using var stream = await opening;
        Assert.Equal(totalCount, stream.TotalCount);
        Assert.Equal(totalBytes, stream.TotalBytes);
        Assert.Equal(0, stream.CurrentCount);
        Assert.Equal(0, stream.CurrentBytes);
        await using var rows = stream.GetAsyncEnumerator(TestContext.Current.CancellationToken);
        Assert.Throws<InvalidOperationException>(() => stream.GetAsyncEnumerator(TestContext.Current.CancellationToken));
        Assert.True(await rows.MoveNextAsync());
        Assert.Equal(1ul, rows.Current.Items[0].AsUInt64());
        Assert.Equal("41", Convert.ToHexString(rows.Current.Items[1].Bytes.AsSpan()));
        Assert.Equal(1, stream.CurrentCount);
        Assert.Equal(firstRowBytes, stream.CurrentBytes);

        Assert.Equal(2u, (await peer.ReadAsync<GetNextRequest>()).BlockNumber);
        var second = rows.MoveNextAsync().AsTask();
        Assert.False(second.IsCompleted);
        await peer.SendAsync(new GetBlockResponse(request.InvokeIdAndPriority, true, 3, [.. bytes[split..]]));
        Assert.True(await second);
        Assert.Equal(2ul, rows.Current.Items[0].AsUInt64());
        Assert.Equal("4243", Convert.ToHexString(rows.Current.Items[1].Bytes.AsSpan()));
        Assert.Equal(2, stream.CurrentCount);
        Assert.Equal(allRowBytes, stream.CurrentBytes);
        Assert.False(await rows.MoveNextAsync());
        await peer.ReleaseAsync(client);
    }

    [Theory]
    [InlineData(Array, 2, null)]
    [InlineData(CompactArray, null, 7)]
    [InlineData("0100", 0, null)]
    [InlineData("13020000", null, 0)]
    public async Task ReadsNormalResponsesIncludingEmptyBuffers(string hex, int? totalCount, int? totalBytes)
    {
        var pair = DuplexApduChannel.CreatePair();
        await using var peer = new SessionPeer(pair.Server);
        await using var client = new DlmsClientSession(pair.Client, SessionPeer.Options());
        await peer.AssociateAsync(client);
        var opening = client.OpenProfileBufferAsync(ProfileName, cancellationToken: TestContext.Current.CancellationToken).AsTask();
        var request = await peer.ReadAsync<LnRequest>();
        var decoded = new AxdrCodec().Decode(new(Convert.FromHexString(hex)));
        Assert.Equal(DecodeStatus.Done, decoded.Status);
        await peer.SendAsync(new LnResponse(CosemOperationKind.Read, request.InvokeIdAndPriority, OperationResult.Ok(decoded.Value)));

        await using var stream = await opening;
        Assert.Equal(totalCount, stream.TotalCount);
        Assert.Equal(totalBytes, stream.TotalBytes);
        var expected = decoded.Value!.Type == DlmsType.Array ? decoded.Value.Items : ((DlmsCompactArray)decoded.Value.Value!).Rows;
        var actual = new List<ulong>();
        await foreach (var row in stream.WithCancellation(TestContext.Current.CancellationToken))
        {
            actual.Add(row.Items[0].AsUInt64());
        }

        Assert.Equal(expected.Select(row => row.Items[0].AsUInt64()), actual);
        Assert.Equal(expected.Length, stream.CurrentCount);
        await peer.ReleaseAsync(client);
    }

    [Theory]
    [InlineData("010202011101", 1)] // Array with one missing row.
    [InlineData("01010201110100", 1)] // Array with trailing data.
    [InlineData("130202110902010141", 0)] // A compact row runs past the declared content length.
    [InlineData("1302000100", 0)] // A nonempty compact payload has a zero-width row type.
    public async Task RejectsMalformedStreamAndFaultsSession(string hex, int validRows)
    {
        var pair = DuplexApduChannel.CreatePair();
        await using var peer = new SessionPeer(pair.Server);
        await using var client = new DlmsClientSession(pair.Client, SessionPeer.Options());
        await peer.AssociateAsync(client);
        var opening = client.OpenProfileBufferAsync(ProfileName, cancellationToken: TestContext.Current.CancellationToken).AsTask();
        var request = await peer.ReadAsync<LnRequest>();
        await peer.SendAsync(new GetBlockResponse(request.InvokeIdAndPriority, true, 1, [.. Convert.FromHexString(hex)]));

        await using var stream = await opening;
        await Assert.ThrowsAsync<DlmsProtocolException>(async () =>
        {
            await foreach (var _ in stream.WithCancellation(TestContext.Current.CancellationToken))
            {
            }
        });
        Assert.Equal(validRows, stream.CurrentCount);
        Assert.Equal(ClientSessionState.Faulted, client.State);
    }

    [Fact]
    public async Task DisposingAnIncompleteProfileReadCancelsTheGet()
    {
        var pair = DuplexApduChannel.CreatePair();
        await using var peer = new SessionPeer(pair.Server);
        await using var client = new DlmsClientSession(pair.Client, SessionPeer.Options());
        await peer.AssociateAsync(client);
        var opening = client.OpenProfileBufferAsync(ProfileName, cancellationToken: TestContext.Current.CancellationToken).AsTask();
        var request = await peer.ReadAsync<LnRequest>();
        await peer.SendAsync(new GetBlockResponse(request.InvokeIdAndPriority, false, 1, [1, 1]));
        await using var stream = await opening;
        await peer.ReadAsync<GetNextRequest>();

        await stream.DisposeAsync();
        Assert.Equal(ClientSessionState.Faulted, client.State);
    }

    [Fact]
    public async Task EnforcesTheMessageLimitAcrossBlocksWithoutAssembly()
    {
        var pair = DuplexApduChannel.CreatePair();
        await using var peer = new SessionPeer(pair.Server);
        await using var client = new DlmsClientSession(pair.Client, SessionPeer.Options() with
        {
            Association = new()
            {
                MaxReceivePduSize = 256
            },
            Limits = new()
            {
                MaxMessageBytes = 256
            }
        });
        await peer.AssociateAsync(client);
        var opening = client.OpenProfileBufferAsync(ProfileName, cancellationToken: TestContext.Current.CancellationToken).AsTask();
        var request = await peer.ReadAsync<LnRequest>();
        // The first block starts an array with a string whose body needs 254 bytes.
        byte[] first = new byte[128];
        Convert.FromHexString("01010981FE").CopyTo(first, 0);
        await peer.SendAsync(new GetBlockResponse(request.InvokeIdAndPriority, false, 1, [.. first]));
        await using var stream = await opening;
        await peer.ReadAsync<GetNextRequest>();
        await peer.SendAsync(new GetBlockResponse(request.InvokeIdAndPriority, true, 2, [.. new byte[131]]));

        await using var rows = stream.GetAsyncEnumerator(TestContext.Current.CancellationToken);
        await Assert.ThrowsAsync<DlmsProtocolException>(async () => await rows.MoveNextAsync());
        Assert.Equal(0, stream.CurrentCount);
        Assert.Equal(ClientSessionState.Faulted, client.State);
    }
}
