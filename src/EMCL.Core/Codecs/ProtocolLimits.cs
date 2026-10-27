namespace EMCL.Core.Codecs;

/// <summary>Configures bounds for encoded messages, nested values, collections, and segmented transfers.</summary>
public sealed record ProtocolLimits
{
    /// <summary>Gets the maximum number of bytes accepted for one message or accumulated transfer; the default is 1 MiB.</summary>
    public int MaxMessageBytes { get; init; } = 1024 * 1024;
    /// <summary>Gets the maximum permitted nesting depth; the default is 64.</summary>
    public int MaxDepth { get; init; } = 64;
    /// <summary>Gets the maximum value-complexity budget or collection capacity; the default is 100,000.</summary>
    public int MaxCollectionItems { get; init; } = 100_000;
    /// <summary>Gets the maximum number of segments or blocks accumulated in one transfer; the default is 4,096.</summary>
    public int MaxBlockCount { get; init; } = 4096;

    /// <summary>Returns the supplied limits after validation, or creates and validates the default limits.</summary>
    /// <param name="value">The limits to validate, or null to select defaults.</param>
    /// <returns>Validated limits; the supplied instance is returned unchanged when non-null.</returns>
    public static ProtocolLimits Resolve(ProtocolLimits? value)
    {
        var result = value ?? new();
        result.Validate();
        return result;
    }

    /// <summary>Rejects nonpositive resource limits.</summary>
    /// <exception cref="ArgumentOutOfRangeException">One or more limits are not positive.</exception>
    public void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxMessageBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxDepth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxCollectionItems);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxBlockCount);
    }
}
