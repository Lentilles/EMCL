using EMCL.Core.Access;
using EMCL.Core.Operations;
using EMCL.Core.Pipeline;
using EMCL.Core.Validation;
using EMCL.Objects.Catalog;

namespace EMCL.Objects.Processing;

/// <summary>Composes operation middleware, access control, conversion, validation, and serialized object-handler execution.</summary>
public sealed class ObjectDispatcher : IDlmsPipeline
{
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
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(handlers);
        this.catalog = catalog;
        this.handlers = handlers;
        this.validator = validator ?? new DefaultValueValidator();
        this.converter = converter ?? new IdentityValueConverter();
        this.access = access ?? new AssociationAccessPolicy();
        OperationDelegate next = DispatchAsync;
        foreach (var component in (middleware ?? []).Reverse())
        {
            var continuation = next;
            next = (operation, context, cancellationToken) => component.InvokeAsync(operation, context, continuation, cancellationToken);
        }

        pipeline = next;
    }

    /// <inheritdoc />
    public ValueTask<OperationResult> ExecuteAsync(CosemOperation operation, OperationContext context, CancellationToken cancellationToken = default) => pipeline(operation, context, cancellationToken);
    private async ValueTask<OperationResult> DispatchAsync(CosemOperation op, OperationContext ctx, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (ctx.LogicalDeviceId != catalog.LogicalDeviceId)
        {
            return new(CosemError.ObjectUndefined, Diagnostic: "Wrong logical device.");
        }

        var obj = catalog.Find(op.LogicalName);
        if (obj == null)
        {
            return new(CosemError.ObjectUndefined);
        }

        if (obj.Descriptor.Key.ClassId != op.ClassId)
        {
            return new(CosemError.ObjectClassInconsistent);
        }

        if (!access.Allows(obj, op, ctx))
        {
            return new(CosemError.ReadWriteDenied);
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
                return new(CosemError.TypeUnmatched, Diagnostic: error);
            }
        }
        catch (Exception e) when (e is ArgumentException or FormatException or InvalidOperationException or OverflowException)
        {
            return new(CosemError.TypeUnmatched, Diagnostic: e.Message);
        }

        var handler = handlers.Resolve(catalog.LogicalDeviceId, obj);
        await obj.OperationLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return op.Kind switch
            {
                CosemOperationKind.Read => await handler.ReadAsync(obj, op, ctx, ct).ConfigureAwait(false),
                CosemOperationKind.Write => await handler.WriteAsync(obj, op, ctx, ct).ConfigureAwait(false),
                CosemOperationKind.Invoke => await handler.InvokeAsync(obj, op, ctx, ct).ConfigureAwait(false),
                _ => new(CosemError.OtherReason)
            };
        }
        catch (Exception e) when (e is ArgumentException or FormatException or InvalidOperationException or OverflowException)
        {
            return new(CosemError.TypeUnmatched, Diagnostic: e.Message);
        }
        finally
        {
            obj.OperationLock.Release();
        }
    }
}
