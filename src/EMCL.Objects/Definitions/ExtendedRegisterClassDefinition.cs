using EMCL.Core.Descriptors;
using EMCL.Core.Objects;
using EMCL.Core.Values;

namespace EMCL.Objects.Definitions;

/// <summary>Constructs reusable ExtendedRegister class metadata.</summary>
public static class ExtendedRegisterClassDefinition
{
    /// <summary>Creates an independent, versioned class descriptor.</summary>
    /// <param name="timestampType">The date-time wire tag, either octet-string or date-time.</param>
    /// <param name="valueConstraint">An optional constraint for register values.</param>
    /// <returns>The class descriptor with the supplied member definitions.</returns>
    public static CosemClassDescriptor Create(DlmsType timestampType, Func<DlmsValue, string?>? valueConstraint = null)
    {
        DefinitionFactory.RequireTimestampType(timestampType);
        return DefinitionFactory.Class(CosemClassIds.ExtendedRegister, ExtendedRegisterMembers.Version0, "Extended Register",
            [DefinitionFactory.LogicalName(), new(ExtendedRegisterMembers.Attributes.Value, "value", Constraint: valueConstraint),
                new(ExtendedRegisterMembers.Attributes.ScalerUnit, "scaler_unit", DlmsType.Structure, Constraint: DefinitionConstraints.ScalerUnit),
                new(ExtendedRegisterMembers.Attributes.Status, "status", Constraint: DefinitionConstraints.RegisterStatus),
                new(ExtendedRegisterMembers.Attributes.CaptureTime, "capture_time", timestampType, Constraint: DefinitionConstraints.DateTime)],
            [new(ExtendedRegisterMembers.Methods.Reset, "reset", DlmsType.Int8)]);
    }
}
