using EMCL.Core.Descriptors;
using EMCL.Core.Values;
using EMCL.Objects.Definitions;

namespace EMCL.Profiles.Spodes.Definitions;

internal static class ExtendedRegisterDefinition
{
    internal static CosemClassDescriptor Create() => ExtendedRegisterClassDefinition.Create(Spodes4Profile.Id, DlmsType.DateTime, DefinitionConstraints.ScalarRegisterValue);
}
