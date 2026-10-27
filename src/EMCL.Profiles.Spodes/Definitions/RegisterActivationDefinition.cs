using EMCL.Core.Descriptors;
using EMCL.Objects.Definitions;

namespace EMCL.Profiles.Spodes.Definitions;

internal static class RegisterActivationDefinition
{
    internal static CosemClassDescriptor Create() => RegisterActivationClassDefinition.Create(Spodes4Profile.Id);
}
