using EMCL.Core.Descriptors;
using EMCL.Objects.Definitions;

namespace EMCL.Profiles.BlueBook.Definitions;

internal static class RegisterActivationDefinition
{
    internal static CosemClassDescriptor Create() => RegisterActivationClassDefinition.Create(BlueBook16Profile.Id);
}
