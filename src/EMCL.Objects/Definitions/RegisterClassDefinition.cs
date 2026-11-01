using EMCL.Core.Descriptors;
using EMCL.Core.Objects;
using EMCL.Core.Values;

namespace EMCL.Objects.Definitions;

/// <summary>Constructs reusable Register class metadata.</summary>
public static class RegisterClassDefinition
{
    /// <summary>Creates an independent, versioned class descriptor.</summary>
    /// <param name="valueConstraint">An optional constraint for register values.</param>
    /// <returns>The class descriptor with the supplied member definitions.</returns>
    public static CosemClassDescriptor Create(Func<DlmsValue, string?>? valueConstraint = null)
    {
        return DefinitionFactory.Class(CosemClassIds.Register, RegisterMembers.Version0, "Register",
            [DefinitionFactory.LogicalName(), new(RegisterMembers.Attributes.Value, "value", Constraint: valueConstraint),
                new(RegisterMembers.Attributes.ScalerUnit, "scaler_unit", DlmsType.Structure, Constraint: DefinitionConstraints.ScalerUnit)],
            [new(RegisterMembers.Methods.Reset, "reset", DlmsType.Int8)]);
    }
}
