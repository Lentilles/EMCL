using EMCL.Core.Objects;
using EMCL.Core.Operations;

namespace EMCL.Objects.Handlers;

/// <summary>Overrides individual attribute or method operations while delegating all other operations to an inner handler.</summary>
/// <param name="inner">The wrapped handler or underlying exception, as appropriate.</param>
public sealed class ObjectHandlerOverrides(ICosemObjectHandler inner) : ICosemObjectHandler
{
    private readonly Dictionary<sbyte, ObjectOperationDelegate> reads = [], writes = [], methods = [];
    /// <summary>Registers a replacement reader for one attribute and returns this decorator.</summary>
    /// <param name="index">The signed attribute or method index.</param>
    /// <param name="handler">The handler or operation delegate to register.</param>
    /// <returns>This decorator, allowing additional member overrides to be chained.</returns>
    public ObjectHandlerOverrides OnRead(sbyte index, ObjectOperationDelegate handler)
    {
        reads[index] = handler;
        return this;
    }

    /// <summary>Registers a replacement writer for one attribute and returns this decorator.</summary>
    /// <param name="index">The signed attribute or method index.</param>
    /// <param name="handler">The handler or operation delegate to register.</param>
    /// <returns>This decorator, allowing additional member overrides to be chained.</returns>
    public ObjectHandlerOverrides OnWrite(sbyte index, ObjectOperationDelegate handler)
    {
        writes[index] = handler;
        return this;
    }

    /// <summary>Registers a replacement implementation for one method and returns this decorator.</summary>
    /// <param name="index">The signed attribute or method index.</param>
    /// <param name="handler">The handler or operation delegate to register.</param>
    /// <returns>This decorator, allowing additional member overrides to be chained.</returns>
    public ObjectHandlerOverrides OnInvoke(sbyte index, ObjectOperationDelegate handler)
    {
        methods[index] = handler;
        return this;
    }

    /// <inheritdoc />
    public ValueTask<OperationResult> ReadAsync(CosemObject cosemObject, CosemOperation operation, OperationContext context, CancellationToken cancellationToken) => reads.TryGetValue(operation.Index, out var handler) ? handler(cosemObject, operation, context, cancellationToken) : inner.ReadAsync(cosemObject, operation, context, cancellationToken);
    /// <inheritdoc />
    public ValueTask<OperationResult> WriteAsync(CosemObject cosemObject, CosemOperation operation, OperationContext context, CancellationToken cancellationToken) => writes.TryGetValue(operation.Index, out var handler) ? handler(cosemObject, operation, context, cancellationToken) : inner.WriteAsync(cosemObject, operation, context, cancellationToken);
    /// <inheritdoc />
    public ValueTask<OperationResult> InvokeAsync(CosemObject cosemObject, CosemOperation operation, OperationContext context, CancellationToken cancellationToken) => methods.TryGetValue(operation.Index, out var handler) ? handler(cosemObject, operation, context, cancellationToken) : inner.InvokeAsync(cosemObject, operation, context, cancellationToken);
}
