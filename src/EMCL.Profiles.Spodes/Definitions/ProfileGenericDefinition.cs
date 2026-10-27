using EMCL.Core.Descriptors;
using EMCL.Objects.Definitions;

namespace EMCL.Profiles.Spodes.Definitions;

internal static class ProfileGenericDefinition
{
    internal static CosemClassDescriptor Create() => ProfileGenericClassDefinition.Create(Spodes4Profile.Id);
}
