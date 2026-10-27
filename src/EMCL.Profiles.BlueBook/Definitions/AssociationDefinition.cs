using EMCL.Core.Descriptors;
using EMCL.Objects.Definitions;

namespace EMCL.Profiles.BlueBook.Definitions;

internal static class AssociationDefinition
{
    internal static CosemClassDescriptor Create() => AssociationClassDefinition.Create(BlueBook16Profile.Id, AssociationLnMembers.Version3);
}
