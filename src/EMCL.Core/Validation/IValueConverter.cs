using EMCL.Core.Objects;
using EMCL.Core.Values;

namespace EMCL.Core.Validation;

/// <summary>Converts incoming write values and method parameters before validation.</summary>
public interface IValueConverter
{
    /// <summary>Converts an incoming attribute value or method parameter before validation and handler execution.</summary>
    /// <param name="cosemObject">The resolved object instance.</param>
    /// <param name="index">The signed attribute or method index.</param>
    /// <param name="value">The incoming write value or method parameter.</param>
    /// <returns>The value passed to validation and handler execution.</returns>
    DlmsValue Convert(CosemObject cosemObject, sbyte index, DlmsValue value);
}
