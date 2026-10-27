using EMCL.Core.Descriptors;
using EMCL.Objects.Definitions;

namespace EMCL.Profiles.Spodes.Definitions;

internal static class AssociationDefinition
{
    internal static CosemClassDescriptor Create() => AssociationClassDefinition.Create(Spodes4Profile.Id, AssociationLnMembers.Version1);
}
