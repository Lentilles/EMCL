using EMCL.Core.Objects;
using EMCL.Core.Values;

namespace EMCL.Objects.Registers;

/// <summary>Calculates demand from an application-fed sample window independently of protocol and storage.</summary>
/// <remarks>A handler may call the calculator concurrently for different objects. Implementations must support concurrent calls and leave object state unchanged.</remarks>
public interface IDemandValueCalculator
{
    /// <summary>Calculates the current demand without mutating the object or retaining mutable sample state.</summary>
    /// <param name="cosemObject">The Demand Register, including its period, scaler, and unit metadata.</param>
    /// <param name="samples">An immutable, chronologically ordered sample snapshot inside the configured window.</param>
    /// <returns>The current demand value permitted by the object's class definition.</returns>
    DlmsValue Calculate(CosemObject cosemObject, IReadOnlyList<DemandSample> samples);
}
