using System.Collections.Immutable;

namespace EMCL.Core.Access;

/// <summary>Groups the attribute and method permissions of an object within one association.</summary>
/// <param name="Attributes">The raw attribute permissions advertised by the association.</param>
/// <param name="Methods">The raw method permissions advertised by the association.</param>
public sealed record ObjectAccessRights(ImmutableArray<AttributeAccess> Attributes, ImmutableArray<MethodAccess> Methods);
