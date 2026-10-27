using EMCL.Core.Codecs;
using EMCL.Core.Objects;
using EMCL.Core.Values;
using EMCL.Protocol.Axdr;

namespace EMCL.Objects.Registers;

internal static class RegisterValues
{
    private const byte SupportedClassVersion = 0;

    internal static bool IsNumeric(DlmsType type) => type is DlmsType.Int8 or DlmsType.Int16 or DlmsType.Int32
        or DlmsType.Int64 or DlmsType.UInt8 or DlmsType.UInt16 or DlmsType.UInt32 or DlmsType.UInt64
        or DlmsType.Float32 or DlmsType.Float64 or >= DlmsType.DeltaInt8 and <= DlmsType.DeltaUInt32;

    internal static DlmsValue? Default(CosemObject cosemObject, sbyte index)
    {
        if (!cosemObject.State.TryGet(index, out var current) || !IsNumeric(current.Type))
        {
            return null;
        }

        object zero = current.Type switch
        {
            DlmsType.Float32 => 0f,
            DlmsType.Float64 => 0d,
            DlmsType.UInt64 => 0UL,
            _ => (object)0L
        };
        return new(current.Type, zero);
    }

    internal static string? Validate(CosemObject cosemObject, sbyte index, DlmsValue value, ProtocolLimits? limits = null)
    {
        try
        {
            _ = new AxdrCodec(limits).EncodeBytes(value);
            var attribute = cosemObject.Descriptor.Attributes.FirstOrDefault(a => a.Index == index);
            if (attribute == null || attribute.Type.HasValue && attribute.Type != value.Type)
            {
                return "The value does not match the declared attribute type.";
            }

            return attribute.Constraint?.Invoke(value);
        }
        catch (Exception error) when (error is ArgumentException or FormatException or InvalidOperationException
            or OverflowException or InvalidCastException or NotSupportedException)
        {
            return error.Message;
        }
    }

    internal static DlmsValue Timestamp(CosemObject cosemObject, sbyte index, DateTimeOffset time)
    {
        var type = cosemObject.Descriptor.Attributes.First(a => a.Index == index).Type;
        if (type is not (DlmsType.OctetString or DlmsType.DateTime))
        {
            throw new InvalidOperationException("The register requires a complete date-time attribute.");
        }

        return new(type.Value, CosemDateTime.FromDateTimeOffset(time).Octets);
    }

    internal static void RequireClass(CosemObject cosemObject, ushort classId)
    {
        ArgumentNullException.ThrowIfNull(cosemObject);
        if (cosemObject.Descriptor.Key.ClassId != classId || cosemObject.Descriptor.Key.ClassVersion != SupportedClassVersion)
        {
            throw new ArgumentException($"Expected interface class {classId}, version 0.", nameof(cosemObject));
        }
    }

    internal static bool IsZeroParameter(DlmsValue? value) => value?.Type == DlmsType.Int8 && value.AsInt64() == 0;
}
