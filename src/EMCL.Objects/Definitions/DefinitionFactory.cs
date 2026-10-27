using System.Collections.Immutable;
using EMCL.Core.Descriptors;
using EMCL.Core.Objects;
using EMCL.Core.Values;

namespace EMCL.Objects.Definitions;

internal static class DefinitionFactory
{
    internal static CosemClassDescriptor Class(string profile, ushort classId, byte version, string name,
        CosemAttributeDescriptor[] attributes, CosemMethodDescriptor[] methods)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profile);
        return new(new(profile, classId, version), name, attributes.ToImmutableArray(), methods.ToImmutableArray());
    }

    internal static CosemAttributeDescriptor LogicalName() => new(CosemObject.LogicalNameAttributeIndex, "logical_name", DlmsType.OctetString,
        Constraint: value => value.Bytes.Length == EMCL.Core.Objects.LogicalName.EncodedLength ? null : "logical_name has six octets.");

    internal static void RequireTimestampType(DlmsType type)
    {
        if (type is not (DlmsType.OctetString or DlmsType.DateTime))
        {
            throw new ArgumentOutOfRangeException(nameof(type), "A complete date-time requires octet-string or date-time encoding.");
        }
    }
}
