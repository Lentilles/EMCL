using EMCL.Core.Objects;

namespace EMCL.Objects.Clock;

/// <summary>Reads and adjusts device-local clock state independently of the operating-system clock.</summary>
public interface IClockSource
{
    /// <summary>Reads the current local time of the specified logical clock.</summary>
    /// <param name="clock">The logical-clock identity or injected clock source.</param>
    /// <returns>The current local clock value and UTC offset.</returns>
    DateTimeOffset GetTime(LogicalName clock);
    /// <summary>Sets the specified logical clock without requiring a change to the operating-system clock.</summary>
    /// <param name="clock">The logical-clock identity or injected clock source.</param>
    /// <param name="value">The value to convert, validate, store, or encode.</param>
    void SetTime(LogicalName clock, DateTimeOffset value);
    /// <summary>Adjusts the specified clock by a signed interval; implementations may override this operation for gradual correction.</summary>
    /// <param name="clock">The logical-clock identity or injected clock source.</param>
    /// <param name="delta">The signed amount by which to shift the logical clock.</param>
    void ShiftTime(LogicalName clock, TimeSpan delta) => SetTime(clock, GetTime(clock) + delta);
}
