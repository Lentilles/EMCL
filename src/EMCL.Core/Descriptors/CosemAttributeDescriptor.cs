using System.Collections.Immutable;
using EMCL.Core.Values;

namespace EMCL.Core.Descriptors;

/// <summary>Describes one COSEM attribute, its wire type, supported selectors, and optional value constraint.</summary>
/// <param name="Index">The signed attribute or method index; negative indices identify proprietary members.</param>
/// <param name="Name">The descriptive name of the class or member.</param>
/// <param name="Type">The DLMS wire type, or null when the definition intentionally leaves the type unspecified.</param>
/// <param name="Selectors">The supported selective-access identifiers.</param>
/// <param name="Constraint">An optional value validator that returns null on success or a diagnostic on failure.</param>
public sealed record CosemAttributeDescriptor(sbyte Index, string Name, DlmsType? Type = null, ImmutableArray<byte> Selectors = default, Func<DlmsValue, string?>? Constraint = null);
