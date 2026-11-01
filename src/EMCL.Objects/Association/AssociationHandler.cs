using System.Collections.Immutable;
using EMCL.Core.Access;
using EMCL.Core.Objects;
using EMCL.Core.Operations;
using EMCL.Core.Values;
using EMCL.Objects.Catalog;
using EMCL.Objects.Definitions;
using EMCL.Objects.Handlers;
using EMCL.Objects.Internal;

namespace EMCL.Objects.Association;

/// <summary>Executes local Association LN visibility and user-list operations while leaving HLS to an external security handler.</summary>
/// <param name="catalog">The logical-device catalog.</param>
/// <param name="importer">The importer used to validate association object-list elements.</param>
/// <param name="state">The permission snapshots bound to local association objects.</param>
/// <param name="objectFactory">An optional factory for association-added objects.</param>
public sealed class AssociationHandler(ObjectCatalog catalog, AssociationImporter importer, AssociationState state, ICosemObjectFactory? objectFactory = null) : StoredValueHandler
{
    private readonly ICosemObjectFactory factory = objectFactory ?? new DefaultCosemObjectFactory();
    private AssociationAccessContext Context(CosemObject obj, OperationContext ctx) => state.Get(obj.LogicalName) ?? ctx.Association ?? new(obj.LogicalName.ToString(), obj.Descriptor.Key.ClassVersion, ImmutableDictionary<LogicalName, ObjectAccessRights>.Empty);
    /// <inheritdoc />
    public override ValueTask<OperationResult> ReadAsync(CosemObject cosemObject, CosemOperation operation, OperationContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (operation.Index != AssociationLnMembers.Attributes.ObjectList)
        {
            return base.ReadAsync(cosemObject, operation, context, cancellationToken);
        }

        var associationContext = Context(cosemObject, context);
        var entries = catalog.Objects.Where(o => associationContext.Rights.ContainsKey(o.LogicalName)).Select(o => Entry(o, associationContext.Rights[o.LogicalName]));
        return ValueTask.FromResult(OperationResult.Ok(DlmsValue.Array(entries.ToArray())));
    }

    /// <inheritdoc />
    public override ValueTask<OperationResult> WriteAsync(CosemObject cosemObject, CosemOperation operation, OperationContext context, CancellationToken cancellationToken) => operation.Index is AssociationLnMembers.Attributes.ObjectList or AssociationLnMembers.Attributes.Secret ? ValueTask.FromResult(new OperationResult(CosemError.ReadWriteDenied)) : base.WriteAsync(cosemObject, operation, context, cancellationToken);
    /// <inheritdoc />
    public override ValueTask<OperationResult> InvokeAsync(CosemObject cosemObject, CosemOperation operation, OperationContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (operation.Index is AssociationLnMembers.Methods.ReplyToHlsAuthentication or AssociationLnMembers.Methods.ChangeHlsSecret)
        {
            return ValueTask.FromResult(new OperationResult(CosemError.ReadWriteDenied, Diagnostic: "HLS requires an external security handler."));
        }

        if (operation.Index is AssociationLnMembers.Methods.AddObject or AssociationLnMembers.Methods.RemoveObject)
        {
            var parsed = importer.ParseEntry(operation.Value!, cosemObject.Descriptor.Key.ClassVersion);
            var associationContext = Context(cosemObject, context);
            var existing = catalog.Find(parsed.Name);
            if (operation.Index == AssociationLnMembers.Methods.AddObject)
            {
                if (existing != null && existing.Descriptor.Key != parsed.Descriptor.Key)
                {
                    return ValueTask.FromResult(new OperationResult(CosemError.ObjectClassInconsistent));
                }

                if (existing == null)
                {
                    catalog.Add(factory.Create(parsed.Descriptor, parsed.Name));
                }

                state.Bind(cosemObject.LogicalName, associationContext with
                {
                    Rights = associationContext.Rights.SetItem(parsed.Name, parsed.Rights)
                });
            }
            else
            {
                if (existing == null || existing.Descriptor.Key != parsed.Descriptor.Key || !associationContext.Rights.ContainsKey(parsed.Name))
                {
                    return ValueTask.FromResult(new OperationResult(CosemError.ObjectUndefined));
                }

                // Removing visibility from one association does not delete a shared device object.
                state.Bind(cosemObject.LogicalName, associationContext with
                {
                    Rights = associationContext.Rights.Remove(parsed.Name)
                });
            }

            return ValueTask.FromResult(OperationResult.Ok());
        }

        if (operation.Index is AssociationLnMembers.Methods.AddUser or AssociationLnMembers.Methods.RemoveUser && cosemObject.Descriptor.Key.ClassVersion == AssociationLnMembers.Version3)
        {
            var user = Schema.Structure(operation.Value!, AssociationStructures.User.Count);
            byte id = checked((byte)Schema.Number(user[AssociationStructures.User.Id], DlmsType.UInt8));
            if (user[AssociationStructures.User.Name].Type is not (DlmsType.VisibleString or DlmsType.Utf8String))
            {
                throw new ArgumentException("Invalid user name.");
            }

            var users = cosemObject.State.TryGet(AssociationLnMembers.Attributes.UserList, out var list) ? list.Items.ToList() : [];
            users.RemoveAll(u => Schema.Number(Schema.Structure(u, AssociationStructures.User.Count)[AssociationStructures.User.Id], DlmsType.UInt8) == id);
            if (operation.Index == AssociationLnMembers.Methods.AddUser)
            {
                users.Add(operation.Value!);
            }

            cosemObject.State.Set(AssociationLnMembers.Attributes.UserList, DlmsValue.Array(users.ToArray()));
            return ValueTask.FromResult(OperationResult.Ok());
        }

        return base.InvokeAsync(cosemObject, operation, context, cancellationToken);
    }

    /// <summary>Builds an A-XDR object_list element from an object identity and explicit association permissions.</summary>
    /// <param name="cosemObject">The resolved object instance.</param>
    /// <param name="rights">The permissions to encode in the object-list element.</param>
    /// <returns>A four-field structure suitable for an Association LN object_list array.</returns>
    public static DlmsValue Entry(CosemObject cosemObject, ObjectAccessRights rights)
    {
        ArgumentNullException.ThrowIfNull(cosemObject);
        ArgumentNullException.ThrowIfNull(rights);
        var attributes = rights.Attributes.Select(EncodeAttributeAccess).ToArray();
        var methods = rights.Methods.Select(permission => DlmsValue.Structure(DlmsValue.Number(DlmsType.Int8, permission.Index), DlmsValue.Number(DlmsType.Enum, permission.RawMode))).ToArray();
        return DlmsValue.Structure(
            DlmsValue.Number(DlmsType.UInt16, cosemObject.Descriptor.Key.ClassId),
            DlmsValue.Number(DlmsType.UInt8, cosemObject.Descriptor.Key.ClassVersion),
            DlmsValue.Octets(cosemObject.LogicalName.ToBytes()),
            DlmsValue.Structure(DlmsValue.Array(attributes), DlmsValue.Array(methods))
        );
    }

    private static DlmsValue EncodeAttributeAccess(AttributeAccess permission)
    {
        var selectors = permission.Selectors.IsEmpty ? DlmsValue.Null : DlmsValue.Array(permission.Selectors.Select(selector => DlmsValue.Number(DlmsType.Int8, unchecked((sbyte)selector))).ToArray());
        return DlmsValue.Structure(DlmsValue.Number(DlmsType.Int8, permission.Index), DlmsValue.Number(DlmsType.Enum, permission.RawMode), selectors);
    }
}
