using System.Collections.Concurrent;
using EMCL.Core.Objects;

namespace EMCL.Objects.Clock;

/// <summary>Maintains an independent time adjustment for each logical clock relative to a TimeProvider.</summary>
/// <remarks>Create a separate source for each logical device. SetTime changes only this source and never sets the host operating-system clock.</remarks>
/// <param name="timeProvider">The time provider used as a baseline; null selects TimeProvider.System.</param>
public sealed class MemoryClockSource(TimeProvider? timeProvider = null) : IClockSource
{
    private readonly TimeProvider timeProvider = timeProvider ?? TimeProvider.System;
    private readonly ConcurrentDictionary<LogicalName, (TimeSpan Delta, TimeSpan Offset)> offsets = new();
    /// <inheritdoc />
    public DateTimeOffset GetTime(LogicalName clock)
    {
        var current = offsets.GetValueOrDefault(clock);
        return (timeProvider.GetUtcNow() + current.Delta).ToOffset(current.Offset);
    }

    /// <inheritdoc />
    public void SetTime(LogicalName clock, DateTimeOffset value) => offsets[clock] = (value - timeProvider.GetUtcNow(), value.Offset);
}
