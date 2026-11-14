using EMCL.Core.Objects;

namespace EMCL.Objects.Spodes;

/// <summary>Stages a complete validated Security Setup key batch for atomic application.</summary>
public interface ISpodesSecurityStore
{
    /// <summary>Persists a pending key batch without making the keys effective.</summary>
    /// <param name="securitySetup">The local Security Setup object.</param>
    /// <param name="keys">The new key material by key identifier. Copy any material retained after the call.</param>
    /// <param name="cancellationToken">A token that cancels staging.</param>
    /// <returns>A callback the application invokes only after transmitting the successful ACTION response.</returns>
    ValueTask<Func<CancellationToken, ValueTask>> StageAsync(LogicalName securitySetup,
        IReadOnlyDictionary<byte, ReadOnlyMemory<byte>> keys, CancellationToken cancellationToken);
}
