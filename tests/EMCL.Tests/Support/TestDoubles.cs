using EMCL.Core.Codecs;
using EMCL.Core.Descriptors;
using EMCL.Core.Objects;
using EMCL.Core.Operations;
using EMCL.Core.Pipeline;
using EMCL.Core.State;
using EMCL.Core.Validation;
using EMCL.Core.Values;
using EMCL.Objects.Handlers;
using EMCL.Protocol.Apdu;
using EMCL.Transport.Channels;
using EMCL.Transport.Streams;

namespace EMCL.Tests.Support;

sealed class ConstantHandler(long value) : StoredValueHandler
{
    public override ValueTask<OperationResult> ReadAsync(CosemObject o, CosemOperation operation, OperationContext context, CancellationToken cancellationToken) => ValueTask.FromResult(OperationResult.Ok(DlmsValue.Number(DlmsType.UInt16, value)));
}

sealed class CountingMiddleware : IDlmsMiddleware
{
    public int Count;
    public ValueTask<OperationResult> InvokeAsync(CosemOperation operation, OperationContext context, OperationDelegate next, CancellationToken cancellationToken)
    {
        Count++;
        return next(operation, context, cancellationToken);
    }
}

sealed class PlusOneConverter : IValueConverter
{
    public DlmsValue Convert(CosemObject o, sbyte index, DlmsValue value) => DlmsValue.Number(value.Type, value.AsInt64() + 1);
}

sealed class RejectValidator : IValueValidator
{
    public string? Validate(CosemObject o, CosemOperation operation) => "Rejected";
}

sealed class CustomStore : IObjectStateStore
{
    private readonly Dictionary<sbyte, DlmsValue> values = [];
    public bool TryGet(sbyte i, out DlmsValue v) => values.TryGetValue(i, out v!);
    public void Set(sbyte i, DlmsValue v) => values[i] = v;
}

sealed class CustomFactory : ICosemObjectFactory
{
    public CosemObject Create(CosemClassDescriptor d, LogicalName n, IObjectStateStore? s = null) => new(d, n, s ?? new CustomStore());
}

sealed class FixedTimeProvider(DateTimeOffset value) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => value;
}

sealed class ChunkTransport(byte[] bytes, int chunk) : IByteTransport
{
    private int position;
    public ValueTask<int> ReadAsync(Memory<byte> output, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        int n = Math.Min(chunk, Math.Min(output.Length, bytes.Length - position));
        bytes.AsMemory(position, n).CopyTo(output);
        position += n;
        return ValueTask.FromResult(n);
    }

    public ValueTask WriteAsync(ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

sealed class CustomChannel : IApduChannel
{
    public bool Sent;
    public ValueTask<ReadOnlyMemory<byte>?> ReceiveAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult<ReadOnlyMemory<byte>?>(new byte[] { 42 });
    public ValueTask SendAsync(ReadOnlyMemory<byte> b, CancellationToken cancellationToken = default)
    {
        Sent = b.Span[0] == 42;
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

sealed class CustomCodec : IIncrementalCodec<DlmsApdu>
{
    public DecodeResult<DlmsApdu> Decode(ReadOnlySequence<byte> b) => DecodeResult<DlmsApdu>.Done(new DataNotification(0, [], DlmsValue.Number(DlmsType.UInt8, b.FirstSpan[0])), 1);
    public void Encode(DlmsApdu a, IBufferWriter<byte> output)
    {
        output.GetSpan(1)[0] = (byte)((DataNotification)a).Body.AsInt64();
        output.Advance(1);
    }
}

sealed class CustomPipeline : IDlmsPipeline
{
    public ValueTask<OperationResult> ExecuteAsync(CosemOperation operation, OperationContext context, CancellationToken cancellationToken = default) => ValueTask.FromResult(OperationResult.Ok(DlmsValue.Number(DlmsType.UInt8, 77)));
}

sealed class ByteSegment : ReadOnlySequenceSegment<byte>
{
    public ByteSegment(ReadOnlyMemory<byte> b) => Memory = b;
    public ByteSegment Append(ReadOnlyMemory<byte> b)
    {
        var n = new ByteSegment(b)
        {
            RunningIndex = RunningIndex + Memory.Length
        };
        Next = n;
        return n;
    }
}
