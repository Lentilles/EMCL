using EMCL.Core.Descriptors;
using EMCL.Objects.Definitions;

namespace EMCL.Profiles.Spodes.Definitions;

internal static class RegisterDefinition
{
    internal static CosemClassDescriptor Create() => RegisterClassDefinition.Create(Spodes4Profile.Id, DefinitionConstraints.ScalarRegisterValue);
}
