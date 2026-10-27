using EMCL.Core.Descriptors;
using EMCL.Core.Values;
using EMCL.Objects.Definitions;

namespace EMCL.Profiles.Spodes.Definitions;

internal static class ClockDefinition
{
    private const int MinimumTimeZoneMinutes = -720;
    private const int MaximumTimeZoneMinutes = 840;

    internal static CosemClassDescriptor Create() => ClockClassDefinition.Create(Spodes4Profile.Id, DlmsType.DateTime, MinimumTimeZoneMinutes, MaximumTimeZoneMinutes);
}
