using EMCL.Core.Descriptors;
using EMCL.Objects.Definitions;

namespace EMCL.Profiles.BlueBook.Definitions;

internal static class RegisterDefinition
{
    internal static CosemClassDescriptor Create() => RegisterClassDefinition.Create(BlueBook16Profile.Id);
}
