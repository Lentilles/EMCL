using EMCL.Core.Descriptors;
using EMCL.Objects.Definitions;

namespace EMCL.Profiles.Spodes.Definitions;

internal static class DataDefinition
{
    internal static CosemClassDescriptor Create() => DataClassDefinition.Create(Spodes4Profile.Id);
}
