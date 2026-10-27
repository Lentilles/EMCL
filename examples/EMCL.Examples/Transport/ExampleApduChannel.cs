using EMCL.Transport.Channels;

namespace EMCL.Examples.Transport;

sealed class ExampleApduChannel(byte[] apdu) : IApduChannel
{
    private bool received;
    public ValueTask<ReadOnlyMemory<byte>?> ReceiveAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (received)
        {
            return ValueTask.FromResult<ReadOnlyMemory<byte>?>(null);
        }

        received = true;
        return ValueTask.FromResult<ReadOnlyMemory<byte>?>(apdu);
    }

    public ValueTask SendAsync(ReadOnlyMemory<byte> message, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Console.WriteLine(Convert.ToHexString(message.Span));
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
