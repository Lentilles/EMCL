using System.Collections.Immutable;

namespace EMCL.Core.Access;

/// <summary>Preserves an attribute access mode and selectors as advertised by an association.</summary>
/// <param name="Index">The signed attribute or method index; negative indices identify proprietary members.</param>
/// <param name="RawMode">The original access-mode octet, interpreted using the association version.</param>
/// <param name="Selectors">The supported selective-access identifiers.</param>
public sealed record AttributeAccess(sbyte Index, byte RawMode, ImmutableArray<byte> Selectors);
