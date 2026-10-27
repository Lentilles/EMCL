using System.Collections.Immutable;
using EMCL.Core.Descriptors;
using EMCL.Core.Values;

namespace EMCL.Objects.Definitions;

/// <summary>Constructs independent standard-profile descriptors using shared descriptor-building algorithms.</summary>
public static class StandardDefinitions
{
    private const int BlueBookMinimumZoneMinutes = -840;
    private const int BlueBookMaximumZoneMinutes = 720;
    private const int SpodesMinimumZoneMinutes = -720;
    private const int SpodesMaximumZoneMinutes = 840;

    /// <summary>Builds the supported descriptors for a selected profile and its standard-specific constraints.</summary>
    /// <param name="profile">The standard-profile identifier assigned to the returned definitions.</param>
    /// <param name="associationVersion">The Association LN version, either 1 or 3.</param>
    /// <param name="spodes">Whether to apply the SPODES-specific types and constraints.</param>
    /// <returns>The supported, independently constructed class descriptors.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The association version is not supported.</exception>
    public static ImmutableArray<CosemClassDescriptor> Create(string profile, byte associationVersion, bool spodes)
    {
        var valueConstraint = spodes ? DefinitionConstraints.ScalarRegisterValue : (Func<DlmsValue, string?>?)null;
        var timestampType = spodes ? DlmsType.DateTime : DlmsType.OctetString;
        return
        [
            DataClassDefinition.Create(profile), RegisterClassDefinition.Create(profile, valueConstraint),
            ExtendedRegisterClassDefinition.Create(profile, timestampType, valueConstraint),
            DemandRegisterClassDefinition.Create(profile, timestampType, valueConstraint),
            RegisterActivationClassDefinition.Create(profile),
            ClockClassDefinition.Create(profile, timestampType, spodes ? SpodesMinimumZoneMinutes : BlueBookMinimumZoneMinutes, spodes ? SpodesMaximumZoneMinutes : BlueBookMaximumZoneMinutes),
            ProfileGenericClassDefinition.Create(profile), AssociationClassDefinition.Create(profile, associationVersion)
        ];
    }
}
