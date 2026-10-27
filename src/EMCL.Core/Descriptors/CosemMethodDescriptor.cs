using EMCL.Core.Values;

namespace EMCL.Core.Descriptors;

/// <summary>Describes one COSEM method and its optional parameter type.</summary>
/// <param name="Index">The signed attribute or method index; negative indices identify proprietary members.</param>
/// <param name="Name">The descriptive name of the class or member.</param>
/// <param name="ParameterType">The required method parameter type, or null for unrestricted parameters.</param>
public sealed record CosemMethodDescriptor(sbyte Index, string Name, DlmsType? ParameterType = null);
