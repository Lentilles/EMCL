using EMCL.Core.Descriptors;
using EMCL.Core.Objects;
using EMCL.Core.Values;

namespace EMCL.Objects.Definitions;

/// <summary>Constructs reusable DemandRegister class metadata with explicit profile choices.</summary>
public static class DemandRegisterClassDefinition
{
    /// <summary>Creates an independent, versioned class descriptor.</summary>
    /// <param name="profile">The standard-profile identifier assigned to the returned class key.</param>
    /// <param name="timestampType">The date-time wire tag, either octet-string or date-time.</param>
    /// <param name="valueConstraint">An optional profile-specific constraint for register values.</param>
    /// <returns>The class descriptor with the supplied profile choices.</returns>
    public static CosemClassDescriptor Create(string profile, DlmsType timestampType, Func<DlmsValue, string?>? valueConstraint = null)
    {
        DefinitionFactory.RequireTimestampType(timestampType);
        return DefinitionFactory.Class(profile, CosemClassIds.DemandRegister, DemandRegisterMembers.Version0, "Demand Register",
            [DefinitionFactory.LogicalName(), new(DemandRegisterMembers.Attributes.CurrentAverageValue, "current_average_value", Constraint: valueConstraint),
                new(DemandRegisterMembers.Attributes.LastAverageValue, "last_average_value", Constraint: valueConstraint),
                new(DemandRegisterMembers.Attributes.ScalerUnit, "scaler_unit", DlmsType.Structure, Constraint: DefinitionConstraints.ScalerUnit),
                new(DemandRegisterMembers.Attributes.Status, "status", Constraint: DefinitionConstraints.RegisterStatus),
                new(DemandRegisterMembers.Attributes.CaptureTime, "capture_time", timestampType, Constraint: DefinitionConstraints.DateTime),
                new(DemandRegisterMembers.Attributes.StartTimeCurrent, "start_time_current", timestampType, Constraint: DefinitionConstraints.DateTime),
                new(DemandRegisterMembers.Attributes.Period, "period", DlmsType.UInt32, Constraint: DefinitionConstraints.Positive),
                new(DemandRegisterMembers.Attributes.NumberOfPeriods, "number_of_periods", DlmsType.UInt16, Constraint: DefinitionConstraints.Positive)],
            [new(DemandRegisterMembers.Methods.Reset, "reset", DlmsType.Int8), new(DemandRegisterMembers.Methods.NextPeriod, "next_period", DlmsType.Int8)]);
    }
}
