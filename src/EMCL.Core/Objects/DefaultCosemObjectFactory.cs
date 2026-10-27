using EMCL.Core.Descriptors;
using EMCL.Core.State;

namespace EMCL.Core.Objects;

/// <summary>Creates universal COSEM objects with an optional caller-provided state store.</summary>
public sealed class DefaultCosemObjectFactory : ICosemObjectFactory
{
    /// <inheritdoc />
    public CosemObject Create(CosemClassDescriptor descriptor, LogicalName logicalName, IObjectStateStore? state = null) => new(descriptor, logicalName, state);
}
