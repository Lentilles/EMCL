using System.Globalization;
using EMCL.Core.Objects;
using EMCL.Core.Values;

namespace EMCL.Objects.Registers;

/// <summary>Computes an equally weighted arithmetic mean of finite instantaneous samples as a float64.</summary>
/// <remarks>Samples are equally weighted regardless of spacing. Energy integration, time-weighted demand, and manufacturer-specific rounding require a different calculator. Conversion to binary64 follows CLR numeric conversion precision.</remarks>
public sealed class ArithmeticMeanDemandCalculator : IDemandValueCalculator
{
    /// <inheritdoc />
    public DlmsValue Calculate(CosemObject cosemObject, IReadOnlyList<DemandSample> samples)
    {
        ArgumentNullException.ThrowIfNull(cosemObject);
        ArgumentNullException.ThrowIfNull(samples);
        if (samples.Count == 0)
        {
            throw new ArgumentException("Demand calculation requires at least one sample.", nameof(samples));
        }

        double maximum = 0;
        foreach (var sample in samples)
        {
            maximum = Math.Max(maximum, Math.Abs(Number(sample.Value)));
        }

        if (maximum == 0)
        {
            return new(DlmsType.Float64, 0d);
        }

        // Scaling prevents otherwise finite means from overflowing their intermediate sum.
        double sum = 0;
        double compensation = 0;
        foreach (var sample in samples)
        {
            double term = Number(sample.Value) / maximum - compensation;
            double next = sum + term;
            compensation = next - sum - term;
            sum = next;
        }

        return new(DlmsType.Float64, Math.Clamp(sum / samples.Count, -1d, 1d) * maximum);
    }

    private static double Number(DlmsValue value)
    {
        if (!RegisterValues.IsNumeric(value.Type))
        {
            throw new ArgumentException("Demand samples must be numeric.", nameof(value));
        }

        double number = Convert.ToDouble(value.Value, CultureInfo.InvariantCulture);
        if (!double.IsFinite(number))
        {
            throw new ArgumentException("Demand samples must be finite.", nameof(value));
        }

        return number;
    }
}
