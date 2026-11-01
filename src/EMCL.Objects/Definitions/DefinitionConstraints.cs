using EMCL.Core.Objects;
using EMCL.Core.Values;
using EMCL.Objects.Internal;

namespace EMCL.Objects.Definitions;

/// <summary>Provides reusable structural and scalar constraints for class definitions.</summary>
public static class DefinitionConstraints
{
    /// <summary>Validates a scaler/unit structure containing an integer8 and an enum.</summary>
    /// <param name="value">The proposed scaler/unit value.</param>
    /// <returns>Null when valid; otherwise a diagnostic.</returns>
    public static string? ScalerUnit(DlmsValue value) => Validate(() =>
    {
        var fields = Schema.Structure(value, RegisterStructures.ScalerUnit.Count);
        _ = Schema.Number(fields[RegisterStructures.ScalerUnit.Scaler], DlmsType.Int8);
        _ = Schema.Number(fields[RegisterStructures.ScalerUnit.Unit], DlmsType.Enum);
    });

    /// <summary>Validates the length of a full COSEM date-time without normalizing special fields.</summary>
    /// <param name="value">An octet-string or date-time value.</param>
    /// <returns>Null when valid; otherwise a diagnostic.</returns>
    public static string? DateTime(DlmsValue value) => (value.Type is DlmsType.OctetString or DlmsType.DateTime)
        && value.Bytes.Length == DlmsValueSizes.DateTime ? null : "A complete date-time has twelve octets.";

    /// <summary>Rejects collection and date/time values for scalar register definitions.</summary>
    /// <param name="value">The proposed register value.</param>
    /// <returns>Null when permitted; otherwise a diagnostic.</returns>
    public static string? ScalarRegisterValue(DlmsValue value) => value.Type is
        DlmsType.Array or DlmsType.Structure or DlmsType.CompactArray or DlmsType.DateTime or DlmsType.Date or DlmsType.Time
        ? "This definition requires a scalar register value without a date/time tag." : null;

    /// <summary>Validates the status choices defined for Extended and Demand Register.</summary>
    /// <param name="value">The proposed register status.</param>
    /// <returns>Null for a supported status type; otherwise a diagnostic.</returns>
    public static string? RegisterStatus(DlmsValue value) => value.Type is DlmsType.Null or DlmsType.BitString
        or DlmsType.UInt32 or DlmsType.OctetString or DlmsType.VisibleString or DlmsType.Utf8String
        or DlmsType.UInt8 or DlmsType.UInt16 or DlmsType.UInt64 ? null : "Unsupported register status type.";

    /// <summary>Checks that an already typed integer is positive.</summary>
    /// <param name="value">The integer value to check.</param>
    /// <returns>Null when positive; otherwise a diagnostic.</returns>
    public static string? Positive(DlmsValue value) => value.AsUInt64() > 0 ? null : "The value must be positive.";

    /// <summary>Validates the structural shape of register assignments, without resolving the device catalog.</summary>
    /// <param name="value">The array of class-ID and logical-name structures.</param>
    /// <returns>Null when structurally valid; otherwise a diagnostic.</returns>
    public static string? RegisterAssignments(DlmsValue value) => Validate(() =>
    {
        foreach (var entry in Schema.Collection(value, DlmsType.Array))
        {
            var fields = Schema.Structure(entry, RegisterStructures.Assignment.Count);
            _ = Schema.Number(fields[RegisterStructures.Assignment.ClassId], DlmsType.UInt16);
            if (fields[RegisterStructures.Assignment.LogicalName].Type != DlmsType.OctetString || fields[RegisterStructures.Assignment.LogicalName].Bytes.Length != LogicalName.EncodedLength)
            {
                throw new FormatException("A register reference requires a six-octet logical name.");
            }
        }
    });

    /// <summary>Validates mask names and unsigned8 index arrays, without resolving assignment indices.</summary>
    /// <param name="value">The array of activation masks.</param>
    /// <returns>Null when structurally valid; otherwise a diagnostic.</returns>
    public static string? ActivationMasks(DlmsValue value) => Validate(() =>
    {
        foreach (var entry in Schema.Collection(value, DlmsType.Array))
        {
            var fields = Schema.Structure(entry, RegisterStructures.Mask.Count);
            if (fields[RegisterStructures.Mask.Name].Type != DlmsType.OctetString)
            {
                throw new FormatException("A mask name must be an octet-string.");
            }

            foreach (var index in Schema.Collection(fields[RegisterStructures.Mask.Indices], DlmsType.Array))
            {
                _ = Schema.Number(index, DlmsType.UInt8);
            }
        }
    });

    private static string? Validate(Action validation)
    {
        try
        {
            validation();
            return null;
        }
        catch (Exception error) when (error is ArgumentException or FormatException or InvalidOperationException or OverflowException)
        {
            return error.Message;
        }
    }
}
