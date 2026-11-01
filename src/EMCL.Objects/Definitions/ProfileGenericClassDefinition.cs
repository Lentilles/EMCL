using EMCL.Core.Descriptors;
using EMCL.Core.Objects;
using EMCL.Core.Values;
using EMCL.Objects.ProfileGeneric;

namespace EMCL.Objects.Definitions;

/// <summary>Constructs reusable ProfileGeneric class metadata.</summary>
public static class ProfileGenericClassDefinition
{
    /// <summary>Creates an independent, versioned class descriptor.</summary>
    /// <returns>The class descriptor with the supplied member definitions.</returns>
    public static CosemClassDescriptor Create()
    {
        return DefinitionFactory.Class(CosemClassIds.ProfileGeneric, ProfileGenericMembers.Version1, "Profile Generic",
            [DefinitionFactory.LogicalName(), new(ProfileGenericMembers.Attributes.Buffer, "buffer", Selectors: [ProfileSelectors.Range, ProfileSelectors.Entry], Constraint: value =>
                    value.Type is DlmsType.Array or DlmsType.CompactArray ? null : "Expected array or compact-array."),
                new(ProfileGenericMembers.Attributes.CaptureObjects, "capture_objects", DlmsType.Array), new(ProfileGenericMembers.Attributes.CapturePeriod, "capture_period", DlmsType.UInt32),
                new(ProfileGenericMembers.Attributes.SortMethod, "sort_method", DlmsType.Enum, Constraint: value => value.AsInt64() is >= (byte)ProfileSortMethod.Fifo and <= (byte)ProfileSortMethod.FarthestFromZero ? null : "Invalid sort method."),
                new(ProfileGenericMembers.Attributes.SortObject, "sort_object", DlmsType.Structure), new(ProfileGenericMembers.Attributes.EntriesInUse, "entries_in_use", DlmsType.UInt32),
                new(ProfileGenericMembers.Attributes.ProfileEntries, "profile_entries", DlmsType.UInt32, Constraint: DefinitionConstraints.Positive)],
            [new(ProfileGenericMembers.Methods.Reset, "reset", DlmsType.Int8), new(ProfileGenericMembers.Methods.Capture, "capture", DlmsType.Int8)]);
    }
}
