using System.Collections.Concurrent;
using System.Collections.Immutable;
using EMCL.Core.Access;
using EMCL.Core.Objects;

namespace EMCL.Objects.Association;

/// <summary>Stores immutable permission snapshots separately for each local association object.</summary>
/// <remarks>Previously returned snapshots are immutable. Obtain a new operation context after association visibility changes.</remarks>
public sealed class AssociationState
{
    private readonly ConcurrentDictionary<LogicalName, AssociationAccessContext> contexts = new();
    /// <summary>Replaces the permission snapshot bound to an association object.</summary>
    /// <param name="associationObject">The association logical name; null selects 0.0.40.0.0.255.</param>
    /// <param name="context">The logical-device, association, and protection context.</param>
    public void Bind(LogicalName associationObject, AssociationAccessContext context) => contexts[associationObject] = context;
    /// <summary>Retrieves the current permission snapshot bound to an association object.</summary>
    /// <param name="associationObject">The association logical name; null selects 0.0.40.0.0.255.</param>
    /// <returns>The current snapshot, or null when the association object has no binding.</returns>
    public AssociationAccessContext? Get(LogicalName associationObject) => contexts.GetValueOrDefault(associationObject);
}
