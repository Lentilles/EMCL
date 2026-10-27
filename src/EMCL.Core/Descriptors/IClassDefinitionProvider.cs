namespace EMCL.Core.Descriptors;

/// <summary>Resolves interface-class definitions without substituting another version or standard profile.</summary>
public interface IClassDefinitionProvider
{
    /// <summary>Finds an exact profile, class identifier, and version match.</summary>
    /// <param name="key">The exact class key or independently scoped GET transfer key.</param>
    /// <returns>The exact matching descriptor, or null when this provider has no definition.</returns>
    CosemClassDescriptor? Find(ClassKey key);
}
