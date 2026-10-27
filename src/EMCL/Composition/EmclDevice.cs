using EMCL.Core.Access;
using EMCL.Core.Descriptors;
using EMCL.Core.Objects;
using EMCL.Core.Operations;
using EMCL.Core.Pipeline;
using EMCL.Objects.Association;
using EMCL.Objects.Catalog;
using EMCL.Objects.Processing;

namespace EMCL.Composition;

/// <summary>Exposes a logical-device catalog, extension registries, association state, and replaceable operation pipeline.</summary>
/// <param name="catalog">The logical-device catalog.</param>
/// <param name="definitions">The provider or registry used to resolve exact class definitions.</param>
/// <param name="factory">The object factory; null selects the universal factory.</param>
/// <param name="handlers">The handler registry used by the dispatcher and capture source.</param>
/// <param name="associations">The association-object permission store.</param>
/// <param name="pipeline">The operation pipeline.</param>
/// <param name="importedAccess">The imported permission snapshot used when no newer association binding exists.</param>
public sealed class EmclDevice(ObjectCatalog catalog, ClassRegistry definitions, ICosemObjectFactory factory, HandlerRegistry handlers, AssociationState associations, IDlmsPipeline pipeline, AssociationAccessContext? importedAccess = null)
{
    /// <summary>Gets the logical-device object catalog.</summary>
    public ObjectCatalog Catalog { get; } = catalog;
    /// <summary>Gets the registry used to resolve class definitions.</summary>
    public ClassRegistry Definitions { get; } = definitions;
    /// <summary>Gets the handler registry used by the composed dispatcher.</summary>
    public HandlerRegistry Handlers { get; } = handlers;
    /// <summary>Gets the current local association permission snapshots.</summary>
    public AssociationState Associations { get; } = associations;
    /// <summary>Gets or sets the complete operation pipeline used by the application.</summary>
    public IDlmsPipeline Pipeline { get; set; } = pipeline;
    /// <summary>Gets a local operation context without association permission checks.</summary>
    /// <remarks>No association snapshot means unrestricted local execution under the selected access policy; use CreateContext for imported association permissions.</remarks>
    public OperationContext LocalContext => new(Catalog.LogicalDeviceId);

    /// <summary>Creates a context from the latest snapshot of the selected association object.</summary>
    /// <remarks>Permission snapshots are immutable. Obtain a fresh context after local association add/remove operations.</remarks>
    /// <param name="associationObject">The association logical name; null selects 0.0.40.0.0.255.</param>
    /// <returns>A context containing the latest matching snapshot or the original imported snapshot.</returns>
    public OperationContext CreateContext(LogicalName? associationObject = null) => new(Catalog.LogicalDeviceId, Associations.Get(associationObject ?? LogicalName.Parse("0.0.40.0.0.255")) ?? importedAccess);
    /// <summary>Creates and registers an object of the requested exact class version, using an untyped descriptor if no definition exists.</summary>
    /// <param name="classId">The interface-class identifier.</param>
    /// <param name="version">The exact class or association version required by this operation.</param>
    /// <param name="logicalName">The six-octet identity of the object.</param>
    /// <returns>The object registered in the logical-device catalog.</returns>
    public CosemObject Add(ushort classId, byte version, LogicalName logicalName)
    {
        var key = new ClassKey(Catalog.ProfileId, classId, version);
        var descriptor = Definitions.Find(key) ?? new(key, $"Unknown class {classId} v{version}", [], [], false);
        var obj = factory.Create(descriptor, logicalName);
        Catalog.Add(obj);
        return obj;
    }
}
