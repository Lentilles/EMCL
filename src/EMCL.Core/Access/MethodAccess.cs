namespace EMCL.Core.Access;

/// <summary>Preserves a method access mode as advertised by an association.</summary>
/// <param name="Index">The signed attribute or method index; negative indices identify proprietary members.</param>
/// <param name="RawMode">The original access-mode octet, interpreted using the association version.</param>
public sealed record MethodAccess(sbyte Index, byte RawMode);
