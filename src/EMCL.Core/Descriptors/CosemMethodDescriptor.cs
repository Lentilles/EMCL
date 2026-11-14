using EMCL.Core.Values;

namespace EMCL.Core.Descriptors;

/// <summary>Describes one COSEM method and its optional parameter type.</summary>
/// <param name="Index">The signed attribute or method index; negative indices identify proprietary members.</param>
/// <param name="Name">The descriptive name of the class or member.</param>
/// <param name="ParameterType">The required method parameter type, or null for unrestricted parameters.</param>
public sealed record CosemMethodDescriptor(sbyte Index, string Name, DlmsType? ParameterType = null)
{
    /// <summary>Gets the optional constraint for the encoded method parameter.</summary>
    public Func<DlmsValue, string?>? ParameterConstraint
    {
        get; init;
    }

    /// <summary>Gets the wire type of the return value when defined by the class.</summary>
    public DlmsType? ResultType
    {
        get; init;
    }

    /// <summary>Gets the optional constraint for the encoded return value.</summary>
    public Func<DlmsValue, string?>? ResultConstraint
    {
        get; init;
    }
}
