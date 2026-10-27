using EMCL.Core.Descriptors;
using EMCL.Core.State;

namespace EMCL.Core.Objects;

/// <summary>Creates object instances independently of their definitions and state-storage implementation.</summary>
public interface ICosemObjectFactory
{
    /// <summary>Creates an object with the supplied definition, identity, and optional attribute store.</summary>
    /// <param name="descriptor">The exact class definition to apply.</param>
    /// <param name="logicalName">The six-octet identity of the object.</param>
    /// <param name="state">An optional attribute store; null lets the factory select its default.</param>
    /// <returns>A new object initialized with the supplied logical name.</returns>
    CosemObject Create(CosemClassDescriptor descriptor, LogicalName logicalName, IObjectStateStore? state = null);
}
