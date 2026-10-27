using EMCL.Core.Objects;
using EMCL.Core.Validation;
using EMCL.Core.Values;

namespace EMCL.Objects.Processing;

/// <summary>Passes incoming DLMS values through without changing their type or content.</summary>
public sealed class IdentityValueConverter : IValueConverter
{
    /// <inheritdoc />
    public DlmsValue Convert(CosemObject cosemObject, sbyte index, DlmsValue value) => value;
}
