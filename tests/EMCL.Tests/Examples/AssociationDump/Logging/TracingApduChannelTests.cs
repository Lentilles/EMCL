using EMCL.Examples.AssociationDump.Logging;
using EMCL.Protocol.Acse;
using EMCL.Protocol.Apdu;
using EMCL.Tests.Support;

namespace EMCL.Tests.Examples.AssociationDump.Logging;

public sealed class TracingApduChannelTests
{
    [Fact]
    public async Task NumbersMessagesInExchangeOrderAndRedactsLowAuthenticationRequest()
    {
        var (client, server) = DuplexApduChannel.CreatePair();
        await using var peer = server;
        using var output = new StringWriter();
        await using var trace = new TracingApduChannel(client, output, redactAarq: true);
        byte[] password = "test-password"u8.ToArray();
        byte[] aarq = [(byte)AcseKind.Aarq, .. password];
        byte[] aare = [(byte)AcseKind.Aare, 1];
        byte[] get = [(byte)ApduTag.GetRequest, 1];

        await trace.SendAsync(aarq, TestContext.Current.CancellationToken);
        Assert.Equal(aarq, (await peer.ReceiveAsync(TestContext.Current.CancellationToken))!.Value.ToArray());
        await peer.SendAsync(aare, TestContext.Current.CancellationToken);
        Assert.Equal(aare, (await trace.ReceiveAsync(TestContext.Current.CancellationToken))!.Value.ToArray());
        await trace.SendAsync(get, TestContext.Current.CancellationToken);

        string[] lines = output.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(3, lines.Length);
        Assert.Equal($"[0001] TX Aarq ({aarq.Length} bytes): [redacted: authentication data]", lines[0]);
        Assert.Equal("[0002] RX Aare (2 bytes): 6101", lines[1]);
        Assert.Equal("[0003] TX GetRequest (2 bytes): C001", lines[2]);
        Assert.DoesNotContain(Convert.ToHexString(password), output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task LogsUnauthenticatedAssociationRequestBytes()
    {
        var (client, server) = DuplexApduChannel.CreatePair();
        await using var peer = server;
        using var output = new StringWriter();
        await using var trace = new TracingApduChannel(client, output, redactAarq: false);
        byte[] aarq = [(byte)AcseKind.Aarq, 0];

        await trace.SendAsync(aarq, TestContext.Current.CancellationToken);

        Assert.Contains("[0001] TX Aarq (2 bytes): 6000", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task RedactsHighAuthenticationActionMessages()
    {
        var (client, server) = DuplexApduChannel.CreatePair();
        await using var peer = server;
        using var output = new StringWriter();
        await using var trace = new TracingApduChannel(client, output, redactAarq: true);

        byte[] request = [(byte)ApduTag.ActionRequest, 1];
        byte[] response = [(byte)ApduTag.ActionResponse, 2];
        await trace.SendAsync(request, TestContext.Current.CancellationToken);
        await peer.SendAsync(response, TestContext.Current.CancellationToken);
        await trace.ReceiveAsync(TestContext.Current.CancellationToken);

        Assert.DoesNotContain("C301", output.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("C702", output.ToString(), StringComparison.Ordinal);
        Assert.Equal(2, output.ToString().Split("[redacted: authentication data]").Length - 1);
    }
}
