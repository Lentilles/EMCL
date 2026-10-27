using EMCL.Core.Descriptors;
using EMCL.Core.Objects;
using EMCL.Core.Values;

namespace EMCL.Objects.Definitions;

/// <summary>Constructs reusable RegisterActivation class metadata with explicit profile choices.</summary>
public static class RegisterActivationClassDefinition
{
    /// <summary>Creates an independent, versioned class descriptor.</summary>
    /// <param name="profile">The standard-profile identifier assigned to the returned class key.</param>
    /// <returns>The class descriptor with the supplied profile choices.</returns>
    public static CosemClassDescriptor Create(string profile)
    {
        return DefinitionFactory.Class(profile, CosemClassIds.RegisterActivation, RegisterActivationMembers.Version0, "Register Activation",
            [DefinitionFactory.LogicalName(),
                new(RegisterActivationMembers.Attributes.RegisterAssignment, "register_assignment", DlmsType.Array, Constraint: DefinitionConstraints.RegisterAssignments),
                new(RegisterActivationMembers.Attributes.MaskList, "mask_list", DlmsType.Array, Constraint: DefinitionConstraints.ActivationMasks),
                new(RegisterActivationMembers.Attributes.ActiveMask, "active_mask", DlmsType.OctetString)],
            [new(RegisterActivationMembers.Methods.AddRegister, "add_register", DlmsType.Structure), new(RegisterActivationMembers.Methods.AddMask, "add_mask", DlmsType.Structure),
                new(RegisterActivationMembers.Methods.DeleteMask, "delete_mask", DlmsType.OctetString)]);
    }
}
