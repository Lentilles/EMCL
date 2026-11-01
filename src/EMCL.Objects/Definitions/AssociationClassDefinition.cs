using EMCL.Core.Descriptors;
using EMCL.Core.Objects;
using EMCL.Core.Values;

namespace EMCL.Objects.Definitions;

/// <summary>Constructs reusable Association class metadata.</summary>
public static class AssociationClassDefinition
{
    /// <summary>Creates an independent, versioned class descriptor.</summary>
    /// <param name="version">The exact Association LN version, either 1 or 3.</param>
    /// <returns>The class descriptor with the supplied member definitions.</returns>
    public static CosemClassDescriptor Create(byte version)
    {
        if (version is not (AssociationLnMembers.Version1 or AssociationLnMembers.Version3))
        {
            throw new ArgumentOutOfRangeException(nameof(version));
        }

        var attributes = new List<CosemAttributeDescriptor>
        {
            DefinitionFactory.LogicalName(), new(AssociationLnMembers.Attributes.ObjectList, "object_list", DlmsType.Array),
            new(AssociationLnMembers.Attributes.AssociatedPartnersId, "associated_partners_id", DlmsType.Structure), new(AssociationLnMembers.Attributes.ApplicationContextName, "application_context_name", DlmsType.Structure),
            new(AssociationLnMembers.Attributes.XdlmsContextInfo, "xDLMS_context_info", DlmsType.Structure), new(AssociationLnMembers.Attributes.AuthenticationMechanismName, "authentication_mechanism_name"),
            new(AssociationLnMembers.Attributes.Secret, "secret", DlmsType.OctetString), new(AssociationLnMembers.Attributes.AssociationStatus, "association_status", DlmsType.Enum),
            new(AssociationLnMembers.Attributes.SecuritySetupReference, "security_setup_reference", DlmsType.OctetString)
        };
        var methods = new List<CosemMethodDescriptor>
        {
            new(AssociationLnMembers.Methods.ReplyToHlsAuthentication, "reply_to_HLS_authentication", DlmsType.OctetString), new(AssociationLnMembers.Methods.ChangeHlsSecret, "change_HLS_secret", DlmsType.OctetString),
            new(AssociationLnMembers.Methods.AddObject, "add_object", DlmsType.Structure), new(AssociationLnMembers.Methods.RemoveObject, "remove_object", DlmsType.Structure)
        };
        if (version == AssociationLnMembers.Version3)
        {
            attributes.Add(new(AssociationLnMembers.Attributes.UserList, "user_list", DlmsType.Array));
            attributes.Add(new(AssociationLnMembers.Attributes.CurrentUser, "current_user", DlmsType.Structure));
            methods.Add(new(AssociationLnMembers.Methods.AddUser, "add_user", DlmsType.Structure));
            methods.Add(new(AssociationLnMembers.Methods.RemoveUser, "remove_user", DlmsType.Structure));
        }

        return DefinitionFactory.Class(CosemClassIds.AssociationLn, version, "Association LN", attributes.ToArray(), methods.ToArray());
    }
}
