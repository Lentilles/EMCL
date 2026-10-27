using EMCL.Core.Access;
using EMCL.Core.Objects;
using EMCL.Core.Operations;
using EMCL.Objects.Definitions;

namespace EMCL.Objects.Processing;

/// <summary>Evaluates Association LN v1 enumerated permissions and v3 protection bitmasks separately.</summary>
public sealed class AssociationAccessPolicy : IAccessPolicy
{
    /// <inheritdoc />
    public bool Allows(CosemObject cosemObject, CosemOperation operation, OperationContext context)
    {
        // No association means explicitly local use; a provided association is always enforced.
        if (context.Association == null)
        {
            return true;
        }

        var association = context.Association;
        if (!association.Rights.TryGetValue(cosemObject.LogicalName, out var rights))
        {
            return false;
        }

        bool method = operation.Kind == CosemOperationKind.Invoke;
        byte mode;
        if (method)
        {
            var right = rights.Methods.FirstOrDefault(m => m.Index == operation.Index);
            if (right == null)
            {
                return false;
            }

            mode = right.RawMode;
        }
        else
        {
            var right = rights.Attributes.FirstOrDefault(a => a.Index == operation.Index);
            if (right == null || operation.Selection != null && !right.Selectors.Contains(operation.Selection.Selector))
            {
                return false;
            }

            mode = right.RawMode;
        }

        if (association.AssociationVersion == AssociationLnMembers.Version1)
        {
            if (method)
            {
                return mode == (byte)MethodAccessModeV1.Access
                    || mode == (byte)MethodAccessModeV1.AuthenticatedAccess && context.AuthenticatedRequest;
            }

            bool requiresAuthentication = mode >= (byte)AttributeAccessModeV1.AuthenticatedReadOnly;
            const int authenticatedModeOffset = (byte)AttributeAccessModeV1.AuthenticatedReadOnly - (byte)AttributeAccessModeV1.ReadOnly;
            int basic = requiresAuthentication ? mode - authenticatedModeOffset : mode;
            int basicAccess = operation.Kind == CosemOperationKind.Read
                ? (byte)AttributeAccessModeV1.ReadOnly : (byte)AttributeAccessModeV1.WriteOnly;
            return (!requiresAuthentication || context.AuthenticatedRequest) && (basic & basicAccess) != 0;
        }

        if (association.AssociationVersion != AssociationLnMembers.Version3)
        {
            return false;
        }

        var flags = (AccessModeV3)mode;
        var access = method ? AccessModeV3.Invoke
            : operation.Kind == CosemOperationKind.Read ? AccessModeV3.Read : AccessModeV3.Write;
        return (flags & access) != AccessModeV3.None
            && ((flags & AccessModeV3.AuthenticatedRequest) == AccessModeV3.None || context.AuthenticatedRequest)
            && ((flags & AccessModeV3.EncryptedRequest) == AccessModeV3.None || context.EncryptedRequest)
            && ((flags & AccessModeV3.SignedRequest) == AccessModeV3.None || context.SignedRequest)
            && ((flags & AccessModeV3.AuthenticatedResponse) == AccessModeV3.None || context.AuthenticatedResponse)
            && ((flags & AccessModeV3.EncryptedResponse) == AccessModeV3.None || context.EncryptedResponse)
            && ((flags & AccessModeV3.SignedResponse) == AccessModeV3.None || context.SignedResponse);
    }
}
