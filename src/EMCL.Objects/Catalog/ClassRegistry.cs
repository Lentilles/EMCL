using EMCL.Core.Descriptors;

namespace EMCL.Objects.Catalog;

/// <summary>Resolves explicit class definitions before consulting registered definition providers in reverse order.</summary>
/// <remarks>Configure registrations before sharing a composed dispatcher between threads.</remarks>
public sealed class ClassRegistry : IClassDefinitionProvider
{
    private readonly List<IClassDefinitionProvider> providers = [];
    private readonly Dictionary<ClassKey, CosemClassDescriptor> definitions = [];
    /// <summary>Appends a definition provider; later providers take precedence over earlier providers.</summary>
    /// <param name="provider">The definition provider to append.</param>
    public void AddProvider(IClassDefinitionProvider provider) => providers.Add(provider);
    /// <summary>Registers or replaces an explicit definition, which takes precedence over all providers.</summary>
    /// <param name="descriptor">The exact class definition to apply.</param>
    public void Register(CosemClassDescriptor descriptor) => definitions[descriptor.Key] = descriptor;
    /// <inheritdoc />
    public CosemClassDescriptor? Find(ClassKey key)
    {
        if (definitions.TryGetValue(key, out var found))
        {
            return found;
        }

        for (int i = providers.Count - 1; i >= 0; i--)
        {
            if (providers[i].Find(key) is { } value)
            {
                return value;
            }
        }

        return null;
    }
}
