using EMCL.Client.Association;
using EMCL.Client.Sessions;
using EMCL.Core.Codecs;
using EMCL.Core.Objects;
using EMCL.Core.Values;
using EMCL.Protocol.Acse;
using EMCL.Protocol.Apdu;
using EMCL.Transport.Channels;

namespace EMCL.Tests.Support;

internal sealed class SessionPeer(IApduChannel channel) : IAsyncDisposable
{
    private readonly ApduCodec codec = new();
    internal static LogicalName RegisterName { get; } = LogicalName.Parse("1.0.1.8.0.255");
    internal static ClientSessionOptions Options(int concurrency = 1) => new()
    {
        RequestTimeout = TimeSpan.FromSeconds(3),
        MaxConcurrentRequests = concurrency
    };

    internal async Task<T> ReadAsync<T>() where T : DlmsApdu
    {
        var bytes = await channel.ReceiveAsync(TestContext.Current.CancellationToken).AsTask()
            .WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.True(bytes.HasValue);
        var result = codec.Decode(new(bytes.Value));
        Assert.Equal(DecodeStatus.Done, result.Status);
        Assert.Equal(bytes.Value.Length, result.Consumed);
        return Assert.IsType<T>(result.Value);
    }

    internal ValueTask SendAsync(DlmsApdu value) => channel.SendAsync(codec.EncodeBytes(value), TestContext.Current.CancellationToken);
    internal ValueTask SendRawAsync(byte[] value) => channel.SendAsync(value, TestContext.Current.CancellationToken);

    internal async Task<AcseApdu> AssociateAsync(DlmsClientSession client,
        DlmsConformance conformance = LnAssociationOptions.SupportedConformance & ~DlmsConformance.PriorityManagement,
        ushort serverPdu = 1024)
    {
        var associating = client.AssociateAsync(TestContext.Current.CancellationToken).AsTask();
        var request = await ReadAsync<AcseApdu>();
        Assert.Equal(AcseKind.Aarq, request.Kind);
        await SendAsync(AcseApdu.Aare(codec.EncodeBytes(new InitiateResponse(null, DlmsNegotiation.Version6,
            (uint)conformance, serverPdu))));
        await associating;
        return request;
    }

    internal async Task ReleaseAsync(DlmsClientSession client)
    {
        var releasing = client.ReleaseAsync(TestContext.Current.CancellationToken).AsTask();
        Assert.Equal(AcseKind.Rlrq, (await ReadAsync<AcseApdu>()).Kind);
        await SendAsync(AcseApdu.Release(response: true));
        await releasing;
    }

    internal static ValueTask<DlmsValue> GetAsync(IDlmsClientSession client, CancellationToken cancellationToken) =>
        client.GetAsync(CosemClassIds.Register, RegisterName, 2, cancellationToken: cancellationToken);

    public ValueTask DisposeAsync() => channel.DisposeAsync();
}
