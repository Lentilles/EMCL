using System.Buffers;

namespace EMCL.Protocol.Internal;

internal sealed class BoundedBufferWriter(int maximum) : IBufferWriter<byte>
{
    private readonly ArrayBufferWriter<byte> buffer = new();
    public int WrittenCount => buffer.WrittenCount;
    public ReadOnlySpan<byte> WrittenSpan => buffer.WrittenSpan;

    public void Advance(int count)
    {
        if (count < 0 || count > maximum - WrittenCount)
        {
            throw new ArgumentException("Message limit exceeded.");
        }

        buffer.Advance(count);
    }

    public Memory<byte> GetMemory(int sizeHint = 0)
    {
        Guard(sizeHint);
        var memory = buffer.GetMemory(sizeHint);
        return memory[..Math.Min(memory.Length, maximum - WrittenCount)];
    }

    public Span<byte> GetSpan(int sizeHint = 0)
    {
        Guard(sizeHint);
        var span = buffer.GetSpan(sizeHint);
        return span[..Math.Min(span.Length, maximum - WrittenCount)];
    }

    private void Guard(int hint)
    {
        if (hint < 0 || Math.Max(1, hint) > maximum - WrittenCount)
        {
            throw new ArgumentException("Message limit exceeded.");
        }
    }
}
