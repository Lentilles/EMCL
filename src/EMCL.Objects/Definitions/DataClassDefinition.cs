using EMCL.Core.Descriptors;
using EMCL.Core.Objects;
using EMCL.Core.Values;

namespace EMCL.Objects.Definitions;

/// <summary>Constructs reusable Data class metadata.</summary>
public static class DataClassDefinition
{
    /// <summary>Creates an independent, versioned class descriptor.</summary>
    /// <returns>The class descriptor with the supplied member definitions.</returns>
    public static CosemClassDescriptor Create()
    {
        return DefinitionFactory.Class(CosemClassIds.Data, DataMembers.Version0, "Data",
            [DefinitionFactory.LogicalName(), new(DataMembers.Attributes.Value, "value")], []);
    }
}
