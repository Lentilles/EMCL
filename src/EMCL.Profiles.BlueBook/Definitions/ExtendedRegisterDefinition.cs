using EMCL.Core.Descriptors;
using EMCL.Core.Values;
using EMCL.Objects.Definitions;

namespace EMCL.Profiles.BlueBook.Definitions;

internal static class ExtendedRegisterDefinition
{
    internal static CosemClassDescriptor Create() => ExtendedRegisterClassDefinition.Create(BlueBook16Profile.Id, DlmsType.OctetString);
}
