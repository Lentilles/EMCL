using System.Collections.Immutable;
using EMCL.Core.Codecs;
using EMCL.Core.Descriptors;
using EMCL.Core.Objects;
using EMCL.Objects.Association;
using EMCL.Objects.Catalog;
using EMCL.Objects.Clock;
using EMCL.Objects.Definitions;
using EMCL.Objects.Processing;
using EMCL.Objects.ProfileGeneric;
using EMCL.Objects.Registers;
using EMCL.Profiles.Spodes.Definitions;

namespace EMCL.Profiles.Spodes;

/// <summary>Provides the supported interface-class definitions and local handlers for SPODES version 4.</summary>
public sealed class Spodes4Profile : IClassDefinitionProvider
{
    /// <summary>Gets the stable identifier used to isolate definitions and handlers belonging to this standard profile.</summary>
    public const string Id = "Spodes.4";
    private readonly ImmutableDictionary<ClassKey, CosemClassDescriptor> definitions = ImmutableArray.Create(
        DataDefinition.Create(),
        RegisterDefinition.Create(),
        ExtendedRegisterDefinition.Create(),
        DemandRegisterDefinition.Create(),
        RegisterActivationDefinition.Create(),
        ClockDefinition.Create(),
        ProfileGenericDefinition.Create(),
        AssociationDefinition.Create()
    ).ToImmutableDictionary(d => d.Key);
    /// <inheritdoc />
    public CosemClassDescriptor? Find(ClassKey key) => definitions.GetValueOrDefault(key);
    /// <summary>Registers the supported local handlers for this profile using the supplied logical-device catalog and extension services.</summary>
    /// <param name="catalog">The logical-device catalog.</param>
    /// <param name="registry">The registry used to resolve class definitions.</param>
    /// <param name="handlers">The handler registry used by the dispatcher and capture source.</param>
    /// <param name="associations">The association-object permission store.</param>
    /// <param name="clock">An optional clock source; null creates an independent in-memory clock.</param>
    /// <param name="captures">An optional capture source; null selects the local catalog source.</param>
    /// <param name="factory">The object factory; null selects the universal factory.</param>
    /// <param name="limits">Optional resource bounds; null selects validated defaults.</param>
    /// <param name="measurementTimeProvider">The register measurement clock; null selects UTC system time.</param>
    /// <param name="demandCalculator">The demand calculation strategy; null selects an equally weighted arithmetic mean.</param>
    public void RegisterHandlers(ObjectCatalog catalog, ClassRegistry registry, HandlerRegistry handlers, AssociationState associations, IClockSource? clock = null, ICaptureValueSource? captures = null, ICosemObjectFactory? factory = null, ProtocolLimits? limits = null, TimeProvider? measurementTimeProvider = null, IDemandValueCalculator? demandCalculator = null) => StandardHandlerRegistration.Register(Id, AssociationLnMembers.Version1, catalog, registry, handlers, associations, clock, captures, factory, limits, measurementTimeProvider, demandCalculator);
}
