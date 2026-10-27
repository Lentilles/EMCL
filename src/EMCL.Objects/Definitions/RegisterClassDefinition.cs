using EMCL.Core.Descriptors;
using EMCL.Core.Objects;
using EMCL.Core.Values;

namespace EMCL.Objects.Definitions;

/// <summary>Constructs reusable Register class metadata with explicit profile choices.</summary>
public static class RegisterClassDefinition
{
    /// <summary>Creates an independent, versioned class descriptor.</summary>
    /// <param name="profile">The standard-profile identifier assigned to the returned class key.</param>
    /// <param name="valueConstraint">An optional profile-specific constraint for register values.</param>
    /// <returns>The class descriptor with the supplied profile choices.</returns>
    public static CosemClassDescriptor Create(string profile, Func<DlmsValue, string?>? valueConstraint = null)
    {
        return DefinitionFactory.Class(profile, CosemClassIds.Register, RegisterMembers.Version0, "Register",
            [DefinitionFactory.LogicalName(), new(RegisterMembers.Attributes.Value, "value", Constraint: valueConstraint),
                new(RegisterMembers.Attributes.ScalerUnit, "scaler_unit", DlmsType.Structure, Constraint: DefinitionConstraints.ScalerUnit)],
            [new(RegisterMembers.Methods.Reset, "reset", DlmsType.Int8)]);
    }
}
