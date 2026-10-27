using EMCL.Core.Descriptors;
using EMCL.Core.Values;
using EMCL.Objects.Definitions;

namespace EMCL.Profiles.BlueBook.Definitions;

internal static class ClockDefinition
{
    private const int MinimumTimeZoneMinutes = -840;
    private const int MaximumTimeZoneMinutes = 720;

    internal static CosemClassDescriptor Create() => ClockClassDefinition.Create(BlueBook16Profile.Id, DlmsType.OctetString, MinimumTimeZoneMinutes, MaximumTimeZoneMinutes);
}
