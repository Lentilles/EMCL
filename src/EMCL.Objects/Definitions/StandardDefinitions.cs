using System.Collections.Immutable;
using EMCL.Core.Descriptors;
using EMCL.Core.Values;

namespace EMCL.Objects.Definitions;

/// <summary>Constructs the default interface-class definitions.</summary>
public static class StandardDefinitions
{
    /// <summary>Creates a provider for the built-in class definitions.</summary>
    /// <returns>A provider that resolves exact class identifiers and versions.</returns>
    public static IClassDefinitionProvider CreateProvider() => CreateProvider(CosemProfile.Cosem);

    /// <summary>Creates an exact-version definition provider for the selected profile.</summary>
    /// <param name="profile">The information model to resolve.</param>
    public static IClassDefinitionProvider CreateProvider(CosemProfile profile) => new Provider(Create(profile));

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
        AssociationClassDefinition.Create(AssociationLnMembers.Version3),
        CommunicationDefinitions.PppSetup(),
        CommunicationDefinitions.PortProtection()
    ];

    /// <summary>Returns the supported exact interface classes in the selected profile.</summary>
    /// <param name="profile">The information model to enumerate.</param>
    public static ImmutableArray<CosemClassDescriptor> Create(CosemProfile profile) => profile switch
    {
        CosemProfile.Cosem => Create(),
        CosemProfile.Spodes4 => SpodesDefinitions.Create(false),
        CosemProfile.Spodes5 => SpodesDefinitions.Create(true),
        _ => throw new ArgumentOutOfRangeException(nameof(profile))
    };

    private sealed class Provider(ImmutableArray<CosemClassDescriptor> descriptors) : IClassDefinitionProvider
    {
        private readonly ImmutableDictionary<ClassKey, CosemClassDescriptor> definitions =
            descriptors.ToImmutableDictionary(definition => definition.Key);

        public CosemClassDescriptor? Find(ClassKey key) => definitions.GetValueOrDefault(key);
    }
}
