using EMCL.Core.Descriptors;
using EMCL.Core.Objects;
using EMCL.Core.Values;

namespace EMCL.Objects.Definitions;

/// <summary>Constructs reusable Data class metadata with explicit profile choices.</summary>
public static class DataClassDefinition
{
    /// <summary>Creates an independent, versioned class descriptor.</summary>
    /// <param name="profile">The standard-profile identifier assigned to the returned class key.</param>
    /// <returns>The class descriptor with the supplied profile choices.</returns>
    public static CosemClassDescriptor Create(string profile)
    {
        return DefinitionFactory.Class(profile, CosemClassIds.Data, DataMembers.Version0, "Data",
            [DefinitionFactory.LogicalName(), new(DataMembers.Attributes.Value, "value")], []);
    }
}
