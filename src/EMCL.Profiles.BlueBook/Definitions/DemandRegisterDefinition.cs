using EMCL.Core.Descriptors;
using EMCL.Core.Values;
using EMCL.Objects.Definitions;

namespace EMCL.Profiles.BlueBook.Definitions;

internal static class DemandRegisterDefinition
{
    internal static CosemClassDescriptor Create() => DemandRegisterClassDefinition.Create(BlueBook16Profile.Id, DlmsType.OctetString);
}
