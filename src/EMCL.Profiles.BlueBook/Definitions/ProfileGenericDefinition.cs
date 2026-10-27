using EMCL.Core.Descriptors;
using EMCL.Objects.Definitions;

namespace EMCL.Profiles.BlueBook.Definitions;

internal static class ProfileGenericDefinition
{
    internal static CosemClassDescriptor Create() => ProfileGenericClassDefinition.Create(BlueBook16Profile.Id);
}
