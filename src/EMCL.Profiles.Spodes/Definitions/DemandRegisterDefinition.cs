using EMCL.Core.Descriptors;
using EMCL.Core.Values;
using EMCL.Objects.Definitions;

namespace EMCL.Profiles.Spodes.Definitions;

internal static class DemandRegisterDefinition
{
    internal static CosemClassDescriptor Create() => DemandRegisterClassDefinition.Create(Spodes4Profile.Id, DlmsType.DateTime, DefinitionConstraints.ScalarRegisterValue);
}
