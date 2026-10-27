using EMCL.Core.Codecs;
using EMCL.Core.Objects;
using EMCL.Objects.Association;
using EMCL.Objects.Catalog;
using EMCL.Objects.Clock;
using EMCL.Objects.Handlers;
using EMCL.Objects.Processing;
using EMCL.Objects.ProfileGeneric;
using EMCL.Objects.Registers;

namespace EMCL.Objects.Definitions;

/// <summary>Registers separate instances of the common local handlers for one selected standard profile.</summary>
public static class StandardHandlerRegistration
{
    /// <summary>Registers the supported standard handlers with independent state for the selected logical device.</summary>
    /// <param name="profile">The selected standard-profile identifier.</param>
    /// <param name="associationVersion">The Association LN version, either 1 or 3.</param>
    /// <param name="catalog">The logical-device catalog.</param>
    /// <param name="definitions">The provider or registry used to resolve exact class definitions.</param>
    /// <param name="handlers">The handler registry used by the dispatcher and capture source.</param>
    /// <param name="associations">The association-object permission store.</param>
    /// <param name="clock">An optional clock source; null creates an independent in-memory clock.</param>
    /// <param name="captures">An optional capture source; null selects the local catalog source.</param>
    /// <param name="factory">The object factory; null selects the universal factory.</param>
    /// <param name="limits">Optional resource bounds; null selects validated defaults.</param>
    /// <param name="measurementTimeProvider">The register measurement clock; null selects UTC system time.</param>
    /// <param name="demandCalculator">The demand calculation strategy; null selects an equally weighted arithmetic mean.</param>
    public static void Register(string profile, byte associationVersion, ObjectCatalog catalog, ClassRegistry definitions, HandlerRegistry handlers, AssociationState associations, IClockSource? clock = null, ICaptureValueSource? captures = null, ICosemObjectFactory? factory = null, ProtocolLimits? limits = null, TimeProvider? measurementTimeProvider = null, IDemandValueCalculator? demandCalculator = null)
    {
        handlers.ForProfileClass(new(profile, CosemClassIds.Data, DataMembers.Version0), new StoredValueHandler());
        handlers.ForProfileClass(new(profile, CosemClassIds.Register, RegisterMembers.Version0), new RegisterHandler());
        handlers.ForProfileClass(new(profile, CosemClassIds.ExtendedRegister, ExtendedRegisterMembers.Version0), new ExtendedRegisterHandler(measurementTimeProvider, limits: limits));
        handlers.ForProfileClass(new(profile, CosemClassIds.DemandRegister, DemandRegisterMembers.Version0), new DemandRegisterHandler(measurementTimeProvider, demandCalculator, limits: limits));
        handlers.ForProfileClass(new(profile, CosemClassIds.RegisterActivation, RegisterActivationMembers.Version0), new RegisterActivationHandler(catalog, limits));
        handlers.ForProfileClass(new(profile, CosemClassIds.Clock, ClockMembers.Version0), new ClockHandler(clock ?? new MemoryClockSource()));
        handlers.ForProfileClass(new(profile, CosemClassIds.ProfileGeneric, ProfileGenericMembers.Version1), new ProfileGenericHandler(catalog, captures ?? new CatalogCaptureValueSource(catalog, handlers), limits));
        handlers.ForProfileClass(new(profile, CosemClassIds.AssociationLn, associationVersion), new AssociationHandler(catalog, new AssociationImporter(definitions, factory, limits), associations, factory));
    }
}
