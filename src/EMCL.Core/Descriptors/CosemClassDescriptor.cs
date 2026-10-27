using System.Collections.Immutable;

namespace EMCL.Core.Descriptors;

/// <summary>Defines the attributes and methods of one exact interface-class version in a standard profile.</summary>
/// <param name="Key">The exact profile, class identifier, and version key.</param>
/// <param name="Name">The descriptive name of the class or member.</param>
/// <param name="Attributes">The attribute definitions belonging to this exact class version.</param>
/// <param name="Methods">The method definitions belonging to this exact class version.</param>
/// <param name="IsKnown">Whether the descriptor comes from a registered definition rather than association-only metadata.</param>
public sealed record CosemClassDescriptor(ClassKey Key, string Name, ImmutableArray<CosemAttributeDescriptor> Attributes, ImmutableArray<CosemMethodDescriptor> Methods, bool IsKnown = true);
