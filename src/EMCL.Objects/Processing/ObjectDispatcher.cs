using EMCL.Core.Access;
using EMCL.Core.Objects;
using EMCL.Core.Operations;
using EMCL.Core.Pipeline;
using EMCL.Core.Validation;
using EMCL.Objects.Catalog;

namespace EMCL.Objects.Processing;

/// <summary>Composes operation middleware, access control, conversion, validation, and serialized object-handler execution.</summary>
public sealed class ObjectDispatcher : IDlmsPipeline
{
    private readonly AsyncLocal<DispatchFrame?> active = new();
    private readonly SemaphoreSlim? deviceGate;
    private readonly ObjectCatalog catalog;
    private readonly HandlerRegistry handlers;
    private readonly IValueValidator validator;
    private readonly IValueConverter converter;
    private readonly IAccessPolicy access;
    private readonly OperationDelegate pipeline;
    /// <summary>Creates a dispatcher with explicit registries and optional validation, conversion, access control, and middleware.</summary>
    /// <param name="catalog">The logical-device catalog.</param>
    /// <param name="handlers">The handler registry used by the dispatcher and capture source.</param>
    /// <param name="validator">An optional custom validator; null selects descriptor-based validation.</param>
    /// <param name="converter">An optional incoming-value converter; null selects identity conversion.</param>
    /// <param name="access">An optional access policy; null selects version-aware association permissions.</param>
    /// <param name="middleware">The middleware components in outermost-to-innermost order.</param>
    public ObjectDispatcher(ObjectCatalog catalog, HandlerRegistry handlers, IValueValidator? validator = null, IValueConverter? converter = null, IAccessPolicy? access = null, IEnumerable<IDlmsMiddleware>? middleware = null)
        : this(catalog, handlers, validator, converter, access, middleware, false)
    {
    }

    /// <summary>Creates a dispatcher with optional device-wide serialization for operations that invoke other objects.</summary>
    /// <param name="catalog">The device catalog.</param>
    /// <param name="handlers">The handler registry.</param>
    /// <param name="validator">The optional value validator.</param>
    /// <param name="converter">The optional incoming-value converter.</param>
    /// <param name="access">The optional access policy.</param>
    /// <param name="middleware">The middleware in registration order.</param>
    /// <param name="serializeDevice">Whether to serialize top-level operations across this logical device.</param>
    public ObjectDispatcher(ObjectCatalog catalog, HandlerRegistry handlers, IValueValidator? validator, IValueConverter? converter,
        IAccessPolicy? access, IEnumerable<IDlmsMiddleware>? middleware, bool serializeDevice)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(handlers);
        this.catalog = catalog;
        this.handlers = handlers;
        this.validator = validator ?? new DefaultValueValidator();
        this.converter = converter ?? new IdentityValueConverter();
        this.access = access ?? new AssociationAccessPolicy();
        deviceGate = serializeDevice ? new(1, 1) : null;
        OperationDelegate next = DispatchAsync;
        foreach (var component in (middleware ?? []).Reverse())
        {
            var continuation = next;
            next = (operation, context, cancellationToken) => component.InvokeAsync(operation, context, continuation, cancellationToken);
        }

        pipeline = next;
    }

    /// <inheritdoc />
    public ValueTask<OperationResult> ExecuteAsync(CosemOperation operation, OperationContext context, CancellationToken cancellationToken = default)
        => deviceGate is null || active.Value is not null
            ? pipeline(operation, context, cancellationToken)
            : ExecuteExclusiveAsync(operation, context, cancellationToken);

    private async ValueTask<OperationResult> ExecuteExclusiveAsync(CosemOperation operation, OperationContext context, CancellationToken ct)
    {
        await deviceGate!.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return await pipeline(operation, context, ct).ConfigureAwait(false);
        }
        finally
        {
            deviceGate.Release();
        }
    }
    private async ValueTask<OperationResult> DispatchAsync(CosemOperation op, OperationContext ctx, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (ctx.LogicalDeviceId != catalog.LogicalDeviceId)
        {
            return new(CosemResultCode.ObjectUndefined, Diagnostic: "Wrong logical device.");
        }

        var obj = catalog.Find(op.LogicalName);
        if (obj == null)
        {
            return new(CosemResultCode.ObjectUndefined);
        }

        if (obj.Descriptor.Key.ClassId != op.ClassId)
        {
            return new(CosemResultCode.ObjectClassInconsistent);
        }

        if (!access.Allows(obj, op, ctx))
        {
            return new(CosemResultCode.ReadWriteDenied);
        }

        try
        {
            if (op.Value != null)
            {
                op = op with
                {
                    Value = converter.Convert(obj, op.Index, op.Value)
                };
            }

            string? error = validator.Validate(obj, op);
            if (error != null)
            {
                return new(CosemResultCode.TypeUnmatched, Diagnostic: error);
            }
        }
        catch (Exception e) when (e is ArgumentException or FormatException or InvalidOperationException or OverflowException)
        {
            return new(CosemResultCode.TypeUnmatched, Diagnostic: e.Message);
        }

        var parent = active.Value;
        for (var frame = parent; frame is not null; frame = frame.Parent)
        {
            if (frame.Name == obj.LogicalName)
            {
                return new(CosemResultCode.ScopeOfAccessViolated, Diagnostic: "Recursive object operation.");
            }
        }

        var handler = handlers.Resolve(catalog.LogicalDeviceId, obj);
        await obj.OperationLock.WaitAsync(ct).ConfigureAwait(false);
        active.Value = new(obj.LogicalName, parent);
        try
        {
            var result = op.Kind switch
            {
                CosemOperationKind.Read => await handler.ReadAsync(obj, op, ctx, ct).ConfigureAwait(false),
                CosemOperationKind.Write => await handler.WriteAsync(obj, op, ctx, ct).ConfigureAwait(false),
                CosemOperationKind.Invoke => await handler.InvokeAsync(obj, op, ctx, ct).ConfigureAwait(false),
                _ => new OperationResult(CosemResultCode.OtherReason)
            };
            if (op.Kind == CosemOperationKind.Invoke && result.Error == CosemResultCode.Success
                && obj.Descriptor.Methods.FirstOrDefault(method => method.Index == op.Index) is { } definition
                && (definition.ResultType is not null || definition.ResultConstraint is not null)
                && (result.Value is not { } returned
                    || definition.ResultType is not null && definition.ResultType != returned.Type
                    || definition.ResultConstraint?.Invoke(returned) is not null))
            {
                return new(CosemResultCode.TypeUnmatched, Diagnostic: "Invalid method return value.");
            }

            return result;
        }
        catch (Exception e) when (e is ArgumentException or FormatException or InvalidOperationException or OverflowException)
        {
            return new(CosemResultCode.TypeUnmatched, Diagnostic: e.Message);
        }
        finally
        {
            active.Value = parent;
            obj.OperationLock.Release();
        }
    }

    private sealed record DispatchFrame(LogicalName Name, DispatchFrame? Parent);
}
