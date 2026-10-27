using System.Collections.Immutable;
using EMCL.Core.Objects;

namespace EMCL.Core.Access;

/// <summary>Represents an immutable snapshot of the object permissions visible through one association.</summary>
/// <param name="AssociationId">The caller-defined identity of the association.</param>
/// <param name="AssociationVersion">The Association LN version used to interpret access modes; supported versions are 1 and 3.</param>
/// <param name="Rights">The permissions indexed by object logical name within this association.</param>
public sealed record AssociationAccessContext(string AssociationId, byte AssociationVersion, ImmutableDictionary<LogicalName, ObjectAccessRights> Rights);
