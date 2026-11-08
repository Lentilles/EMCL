using EMCL.Core.Access;
using EMCL.Core.Objects;
using EMCL.Core.Operations;
using EMCL.Core.Values;
using EMCL.Objects.Catalog;
using EMCL.Objects.Definitions;
using EMCL.Objects.Processing;

namespace EMCL.Objects.ProfileGeneric;

/// <summary>Reads capture values through handlers resolved from the local catalog and checks association permissions.</summary>
/// <param name="catalog">The catalog containing the capture source objects.</param>
/// <param name="handlers">The handler registry used by the dispatcher and capture source.</param>
/// <param name="accessPolicy">An optional access policy; null selects version-aware association permissions.</param>
public sealed class CatalogCaptureValueSource(ObjectCatalog catalog, HandlerRegistry handlers, IAccessPolicy? accessPolicy = null) : ICaptureValueSource
{
    private readonly IAccessPolicy access = accessPolicy ?? new AssociationAccessPolicy();
    /// <inheritdoc />
    public async ValueTask<OperationResult> ReadAsync(CaptureObject capture, OperationContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var obj = catalog.Find(capture.LogicalName);
        if (obj == null || obj.Descriptor.Key.ClassId != capture.ClassId)
        {
            return new(CosemResultCode.ObjectUndefined);
        }

        async ValueTask<OperationResult> Read(sbyte index)
        {
            var op = new CosemOperation(CosemOperationKind.Read, capture.ClassId, capture.LogicalName, index);
            if (!access.Allows(obj, op, context))
            {
                return new(CosemResultCode.ReadWriteDenied);
            }

            return await handlers.Resolve(catalog.LogicalDeviceId, obj).ReadAsync(obj, op, context, cancellationToken).ConfigureAwait(false);
        }

        OperationResult result;
        if (capture.AttributeIndex == 0)
        {
            var values = new List<DlmsValue>();
            foreach (var attr in obj.Descriptor.Attributes.Where(a => a.Index > 0))
            {
                var item = await Read(attr.Index).ConfigureAwait(false);
                if (item.Error != CosemResultCode.Success)
                {
                    return item;
                }

                values.Add(item.Value!);
            }

            result = OperationResult.Ok(DlmsValue.Structure(values.ToArray()));
        }
        else
        {
            result = await Read(capture.AttributeIndex).ConfigureAwait(false);
        }

        if (result.Error != CosemResultCode.Success || capture.DataIndex == 0)
        {
            return result;
        }

        var value = result.Value!;
        if (obj.Descriptor.Key.ClassId == CosemClassIds.ProfileGeneric && capture.AttributeIndex == ProfileGenericMembers.Attributes.Buffer)
        {
            var rows = ProfileGenericHandler.GetRows(value);
            if (rows.Any(row => row.Type != DlmsType.Structure || row.Items.Length < capture.DataIndex))
            {
                return new(CosemResultCode.ScopeOfAccessViolated);
            }

            return OperationResult.Ok(DlmsValue.Array(rows.Select(row => row.Items[capture.DataIndex - 1]).ToArray()));
        }

        return value.Type == DlmsType.Structure ? capture.DataIndex <= value.Items.Length ? OperationResult.Ok(value.Items[capture.DataIndex - 1]) : new(CosemResultCode.ScopeOfAccessViolated) : result;
    }
}
