using EMCL.Core.Objects;
using EMCL.Core.Operations;

namespace EMCL.Core.Validation;

/// <summary>Validates an operation against its object definition before execution.</summary>
public interface IValueValidator
{
    /// <summary>Checks an operation and returns a diagnostic when it violates the object definition.</summary>
    /// <param name="cosemObject">The resolved object instance.</param>
    /// <param name="operation">The requested object operation.</param>
    /// <returns>Null when the operation is valid; otherwise a diagnostic explaining the violation.</returns>
    string? Validate(CosemObject cosemObject, CosemOperation operation);
}
