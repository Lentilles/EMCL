using EMCL.Core.Codecs;
using EMCL.Core.Objects;
using EMCL.Core.Operations;
using EMCL.Core.Validation;
using EMCL.Protocol.Axdr;

namespace EMCL.Objects.Processing;

/// <summary>Checks declared members, selector support, wire encodability, and descriptor constraints.</summary>
/// <param name="limits">Optional resource bounds; null selects validated defaults.</param>
public sealed class DefaultValueValidator(ProtocolLimits? limits = null) : IValueValidator
{
    /// <inheritdoc />
    public string? Validate(CosemObject cosemObject, CosemOperation operation)
    {
        if (operation.Value != null)
        {
            try
            {
                _ = new AxdrCodec(limits).EncodeBytes(operation.Value);
            }
            catch (Exception e) when (e is ArgumentException or InvalidCastException or OverflowException or FormatException)
            {
                return e.Message;
            }
        }

        if (operation.Kind == CosemOperationKind.Invoke)
        {
            var method = cosemObject.Descriptor.Methods.FirstOrDefault(m => m.Index == operation.Index);
            if (method == null)
            {
                return "Method is not declared.";
            }

            if (method.ParameterType.HasValue && operation.Value?.Type != method.ParameterType)
            {
                return "Method parameter type mismatch.";
            }

            return method.ParameterConstraint is { } constraint
                ? operation.Value is { } value ? constraint(value) : "Method parameter is missing."
                : null;
        }

        var attr = cosemObject.Descriptor.Attributes.FirstOrDefault(a => a.Index == operation.Index);
        if (attr == null)
        {
            return "Attribute is not declared.";
        }

        if (operation.Selection != null && (attr.Selectors.IsDefaultOrEmpty || !attr.Selectors.Contains(operation.Selection.Selector)))
        {
            return "Unsupported access selector.";
        }

        if (operation.Kind != CosemOperationKind.Write)
        {
            return null;
        }

        if (operation.Index == CosemObject.LogicalNameAttributeIndex)
        {
            return "logical_name is immutable.";
        }

        if (operation.Value == null)
        {
            return "Write requires a value.";
        }

        if (attr.Type.HasValue && operation.Value.Type != attr.Type.Value)
        {
            return "Attribute type mismatch.";
        }

        return attr.Constraint?.Invoke(operation.Value);
    }
}
