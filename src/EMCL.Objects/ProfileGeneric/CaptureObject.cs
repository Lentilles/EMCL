using EMCL.Core.Objects;
using EMCL.Core.Values;
using EMCL.Objects.Internal;

namespace EMCL.Objects.ProfileGeneric;

/// <summary>Identifies an attribute or attribute component captured by a Profile Generic object.</summary>
/// <param name="ClassId">The 16-bit interface-class identifier.</param>
/// <param name="LogicalName">The six-octet logical name of the target object.</param>
/// <param name="AttributeIndex">The signed capture attribute index; zero selects all public attributes.</param>
/// <param name="DataIndex">The one-based structure component or profile column; zero selects the whole attribute.</param>
public readonly record struct CaptureObject(ushort ClassId, LogicalName LogicalName, sbyte AttributeIndex, ushort DataIndex)
{
    /// <summary>Parses a class identifier, logical name, signed attribute index, and data index from a four-field structure.</summary>
    /// <param name="value">The value to convert, validate, store, or encode.</param>
    public static CaptureObject Parse(DlmsValue value)
    {
        var f = Schema.Structure(value, ProfileStructures.CaptureObject.Count);
        if (f[ProfileStructures.CaptureObject.LogicalName].Type != DlmsType.OctetString)
        {
            throw new FormatException("Capture logical_name must be an octet-string.");
        }

        return new(
            checked((ushort)Schema.Number(f[ProfileStructures.CaptureObject.ClassId], DlmsType.UInt16)),
            LogicalName.FromBytes(f[ProfileStructures.CaptureObject.LogicalName].Bytes.AsSpan()),
            checked((sbyte)Schema.Number(f[ProfileStructures.CaptureObject.AttributeIndex], DlmsType.Int8)),
            checked((ushort)Schema.Number(f[ProfileStructures.CaptureObject.DataIndex], DlmsType.UInt16))
        );
    }
}
