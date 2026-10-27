using EMCL.Core.Access;
using EMCL.Objects.Catalog;

namespace EMCL.Objects.Association;

/// <summary>Contains a completely validated object catalog and the association permissions imported with it.</summary>
/// <param name="Catalog">The completely validated logical-device catalog.</param>
/// <param name="Access">The imported association permission snapshot.</param>
public sealed record ImportedAssociation(ObjectCatalog Catalog, AssociationAccessContext Access);
