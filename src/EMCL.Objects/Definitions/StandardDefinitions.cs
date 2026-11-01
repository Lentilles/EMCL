using System.Collections.Immutable;
using EMCL.Core.Descriptors;
using EMCL.Core.Values;

namespace EMCL.Objects.Definitions;

/// <summary>Constructs the default interface-class definitions.</summary>
public static class StandardDefinitions
{
    /// <summary>Creates a provider for the built-in class definitions.</summary>
    /// <returns>A provider that resolves exact class identifiers and versions.</returns>
    public static IClassDefinitionProvider CreateProvider() => new Provider();

    /// <summary>Builds independent descriptors for supported class versions.</summary>
    /// <returns>The supported class descriptors.</returns>
    public static ImmutableArray<CosemClassDescriptor> Create() =>
    [
        DataClassDefinition.Create(), RegisterClassDefinition.Create(),
        ExtendedRegisterClassDefinition.Create(DlmsType.OctetString),
        DemandRegisterClassDefinition.Create(DlmsType.OctetString),
        RegisterActivationClassDefinition.Create(),
        ClockClassDefinition.Create(DlmsType.OctetString, -840, 720),
        ProfileGenericClassDefinition.Create(),
        AssociationClassDefinition.Create(AssociationLnMembers.Version1),
        AssociationClassDefinition.Create(AssociationLnMembers.Version3)
    ];

    private sealed class Provider : IClassDefinitionProvider
    {
        private readonly ImmutableDictionary<ClassKey, CosemClassDescriptor> definitions =
            Create().ToImmutableDictionary(definition => definition.Key);

        public CosemClassDescriptor? Find(ClassKey key) => definitions.GetValueOrDefault(key);
    }
}
