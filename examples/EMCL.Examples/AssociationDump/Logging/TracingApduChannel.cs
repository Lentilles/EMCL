using EMCL.Protocol.Acse;
using EMCL.Protocol.Apdu;
using EMCL.Transport.Channels;

namespace EMCL.Examples.AssociationDump.Logging;

internal sealed class TracingApduChannel(IApduChannel inner, TextWriter output, bool redactAarq) : IApduChannel
{
    private readonly IApduChannel inner = inner ?? throw new ArgumentNullException(nameof(inner));
    private readonly TextWriter output = output ?? throw new ArgumentNullException(nameof(output));
    private readonly object logLock = new();
    private int messageCount;

    public async ValueTask<ReadOnlyMemory<byte>?> ReceiveAsync(CancellationToken cancellationToken = default)
    {
        var message = await inner.ReceiveAsync(cancellationToken);
        if (message.HasValue)
        {
            Log("RX", message.Value);
        }

        return message;
    }

    public async ValueTask SendAsync(ReadOnlyMemory<byte> apdu, CancellationToken cancellationToken = default)
    {
        Log("TX", apdu);
        await inner.SendAsync(apdu, cancellationToken);
    }

    public ValueTask DisposeAsync() => inner.DisposeAsync();

    private void Log(string direction, ReadOnlyMemory<byte> message)
    {
        ReadOnlySpan<byte> bytes = message.Span;
        string kind = bytes.IsEmpty ? "Empty" : Enum.GetName((AcseKind)bytes[0])
            ?? Enum.GetName((ApduTag)bytes[0]) ?? "Unknown";
        bool authenticationExchange = !bytes.IsEmpty && (bytes[0] == (byte)AcseKind.Aarq
            || bytes[0] is (byte)ApduTag.ActionRequest or (byte)ApduTag.ActionResponse);
        string contents = redactAarq && authenticationExchange
            ? "[redacted: authentication data]"
            : Convert.ToHexString(bytes);

        lock (logLock)
        {
            output.WriteLine($"[{++messageCount:D4}] {direction} {kind} ({bytes.Length} bytes): {contents}");
        }
    }
}
