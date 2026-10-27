using EMCL.Core.Descriptors;
using EMCL.Objects.Definitions;

namespace EMCL.Profiles.BlueBook.Definitions;

internal static class DataDefinition
{
    internal static CosemClassDescriptor Create() => DataClassDefinition.Create(BlueBook16Profile.Id);
}
