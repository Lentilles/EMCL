using EMCL.Core.Objects;
using EMCL.Core.Operations;

namespace EMCL.Core.Access;

/// <summary>Decides whether an operation is permitted by the supplied association and protection context.</summary>
public interface IAccessPolicy
{
    /// <summary>Determines whether the context permits the requested member access and protection requirements.</summary>
    /// <param name="cosemObject">The resolved object instance.</param>
    /// <param name="operation">The requested object operation.</param>
    /// <param name="context">The logical-device, association, and protection context.</param>
    /// <returns>True when the operation is permitted; otherwise false.</returns>
    bool Allows(CosemObject cosemObject, CosemOperation operation, OperationContext context);
}
