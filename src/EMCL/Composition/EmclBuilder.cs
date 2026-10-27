using EMCL.Core.Access;
using EMCL.Core.Codecs;
using EMCL.Core.Objects;
using EMCL.Core.Pipeline;
using EMCL.Core.Validation;
using EMCL.Objects.Association;
using EMCL.Objects.Catalog;
using EMCL.Objects.Clock;
using EMCL.Objects.Processing;
using EMCL.Objects.ProfileGeneric;
using EMCL.Objects.Registers;
using EMCL.Profiles.BlueBook;
using EMCL.Profiles.Spodes;

namespace EMCL.Composition;

/// <summary>Composes standard definitions, local handlers, and an overridable operation pipeline for a logical device.</summary>
/// <remarks>Configure the builder and registries before concurrent use. The built-in profiles register independent local handler instances.</remarks>
public sealed class EmclBuilder
{
    /// <summary>Gets the class-definition registry used by object creation and association import.</summary>
    public ClassRegistry Definitions { get; } = new();
    /// <summary>Gets or sets the factory used to create imported and locally added objects.</summary>
    public ICosemObjectFactory Factory { get; set; } = new DefaultCosemObjectFactory();
    /// <summary>Gets or sets the limits used by the composed importer, handlers, and value validation.</summary>
    public ProtocolLimits Limits { get; set; } = new();
    /// <summary>Gets or sets a custom operation validator; null selects descriptor-based validation.</summary>
    public IValueValidator? Validator
    {
        get; set;
    }
    /// <summary>Gets or sets a custom incoming-value converter; null selects identity conversion.</summary>
    public IValueConverter? Converter
    {
        get; set;
    }
    /// <summary>Gets or sets a custom access policy; null selects version-aware association checks.</summary>
    public IAccessPolicy? AccessPolicy
    {
        get; set;
    }
    /// <summary>Gets the middleware components in their outermost-to-innermost registration order.</summary>
    public IList<IDlmsMiddleware> Middleware { get; } = [];
    /// <summary>Gets or sets the factory that supplies an independent clock source for each composed device.</summary>
    public Func<IClockSource> ClockFactory { get; set; } = () => new MemoryClockSource();

    /// <summary>Gets or sets the source of register measurement timestamps; system UTC is the default.</summary>
    public TimeProvider MeasurementTimeProvider { get; set; } = TimeProvider.System;
    /// <summary>Gets or sets the factory supplying an independent demand calculator for each logical device.</summary>
    public Func<IDemandValueCalculator> DemandCalculatorFactory { get; set; } = () => new ArithmeticMeanDemandCalculator();

    /// <summary>Creates a builder with the supported Blue Book and SPODES definition providers registered.</summary>
    public EmclBuilder()
    {
        Definitions.AddProvider(new BlueBook16Profile());
        Definitions.AddProvider(new Spodes4Profile());
    }

    /// <summary>Creates an importer using the current definitions, object factory, and limits.</summary>
    /// <returns>An importer using the current builder configuration.</returns>
    public AssociationImporter CreateImporter() => new(Definitions, Factory, Limits);
    /// <summary>Builds a logical device, optionally attaching a previously imported catalog and association snapshot.</summary>
    /// <param name="logicalDeviceId">The caller-defined logical-device identity.</param>
    /// <param name="profileId">The standard-profile identifier, including its edition.</param>
    /// <param name="imported">An optional previously imported catalog and association permission snapshot.</param>
    /// <param name="associationObject">The association logical name; null selects 0.0.40.0.0.255.</param>
    /// <param name="captures">An optional capture source; null selects the local catalog source.</param>
    /// <returns>A composed logical device with registered standard handlers when the profile is built in.</returns>
    public EmclDevice Build(string logicalDeviceId, string profileId, ImportedAssociation? imported = null, LogicalName? associationObject = null, ICaptureValueSource? captures = null)
    {
        Limits.Validate();
        var catalog = imported?.Catalog ?? new ObjectCatalog(logicalDeviceId, profileId);
        if (catalog.LogicalDeviceId != logicalDeviceId || catalog.ProfileId != profileId)
        {
            throw new ArgumentException("Imported catalog identity mismatch.");
        }

        var handlers = new HandlerRegistry();
        var associations = new AssociationState();
        captures ??= new CatalogCaptureValueSource(catalog, handlers, AccessPolicy);
        if (profileId == BlueBook16Profile.Id)
        {
            new BlueBook16Profile().RegisterHandlers(catalog, Definitions, handlers, associations, ClockFactory(), captures, Factory, Limits, MeasurementTimeProvider, DemandCalculatorFactory());
        }
        else if (profileId == Spodes4Profile.Id)
        {
            new Spodes4Profile().RegisterHandlers(catalog, Definitions, handlers, associations, ClockFactory(), captures, Factory, Limits, MeasurementTimeProvider, DemandCalculatorFactory());
        }

        if (imported != null)
        {
            associations.Bind(associationObject ?? LogicalName.Parse("0.0.40.0.0.255"), imported.Access);
        }

        return new(
            catalog,
            Definitions,
            Factory,
            handlers,
            associations,
            new ObjectDispatcher(catalog, handlers, Validator ?? new DefaultValueValidator(Limits), Converter, AccessPolicy, Middleware),
            imported?.Access
        );
    }
}
