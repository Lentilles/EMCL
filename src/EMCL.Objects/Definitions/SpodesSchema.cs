using EMCL.Core.Descriptors;
using EMCL.Core.Values;

namespace EMCL.Objects.Definitions;

internal static class SpodesSchema
{
    internal sealed record ValueShape(DlmsType? Type, Func<DlmsValue, string?>? Constraint = null)
    {
        public static implicit operator ValueShape(DlmsType type) => new(type);

        internal string? Validate(DlmsValue value) => Type is not null && value.Type != Type
            ? $"Expected {Type}, got {value.Type}." : Constraint?.Invoke(value);
    }

    internal static ValueShape Any { get; } = new(null);
    internal static ValueShape Ln { get; } = Octets(6);
    internal static ValueShape DateTimeOctets { get; } = Octets(12);
    internal static ValueShape DateOctets { get; } = Octets(5);
    internal static ValueShape TimeOctets { get; } = Octets(4);
    internal static ValueShape Zero { get; } = Range(DlmsType.Int8, 0, 0);
    internal static ValueShape ScriptReference { get; } = Structure(Ln, DlmsType.UInt16);
    internal static ValueShape MonitoredReference { get; } = Structure(DlmsType.UInt16, Ln, DlmsType.Int8);
    internal static ValueShape ActionSet { get; } = Structure(ScriptReference, ScriptReference);

    internal static ValueShape Of(DlmsType type) => new(type);
    internal static ValueShape Range(DlmsType type, long minimum, long maximum)
        => new(type, value => value.AsInt64() >= minimum && value.AsInt64() <= maximum
            ? null : $"Value must be in the range {minimum} through {maximum}.");
    internal static ValueShape Octets(int length, bool allowEmpty = false) => new(DlmsType.OctetString,
        value => value.Bytes.Length == length || allowEmpty && value.Bytes.IsEmpty ? null : $"Expected {length} octets.");
    internal static ValueShape OctetsAtMost(int length) => new(DlmsType.OctetString,
        value => value.Bytes.Length <= length ? null : $"Expected at most {length} octets.");
    internal static ValueShape Text(int length) => new(DlmsType.VisibleString,
        value => value.Value is string text && text.Length <= length ? null : $"Expected at most {length} characters.");
    internal static ValueShape OneOf(params ValueShape[] shapes)
        => new(null, value => shapes.Any(shape => shape.Validate(value) is null) ? null : "Value does not match any allowed shape.");
    internal static ValueShape Array(ValueShape element, int maximum = int.MaxValue)
        => new(DlmsType.Array, value =>
        {
            if (value.Items.Length > maximum)
            {
                return $"Expected at most {maximum} entries.";
            }

            for (int i = 0; i < value.Items.Length; i++)
            {
                if (element.Validate(value.Items[i]) is { } error)
                {
                    return $"Entry {i}: {error}";
                }
            }

            return null;
        });
    internal static ValueShape Structure(params ValueShape[] fields)
        => new(DlmsType.Structure, value =>
        {
            if (value.Items.Length != fields.Length)
            {
                return $"Expected {fields.Length} fields.";
            }

            for (int i = 0; i < fields.Length; i++)
            {
                if (fields[i].Validate(value.Items[i]) is { } error)
                {
                    return $"Field {i + 1}: {error}";
                }
            }

            return null;
        });

    internal static CosemAttributeDescriptor Attr(sbyte index, string name, ValueShape shape)
        => new(index, name, shape.Type, Constraint: shape.Constraint);

    internal static CosemMethodDescriptor Method(sbyte index, string name, ValueShape parameter, ValueShape? result = null)
        => new(index, name, parameter.Type)
        {
            ParameterConstraint = parameter.Validate,
            ResultType = result?.Type,
            ResultConstraint = result is null ? null : result.Validate
        };

    internal static CosemMethodDescriptor Command(sbyte index, string name)
        => Method(index, name, Zero);

    internal static CosemClassDescriptor Class(ushort id, byte version, string name,
        CosemAttributeDescriptor[] attributes, params CosemMethodDescriptor[] methods)
        => DefinitionFactory.Class(id, version, name, [DefinitionFactory.LogicalName(), .. attributes], methods);
}
