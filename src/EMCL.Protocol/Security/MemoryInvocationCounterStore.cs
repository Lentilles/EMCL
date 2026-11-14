using System.Security.Cryptography;

namespace EMCL.Protocol.Security;

/// <summary>Maintains counters in memory for the lifetime of one store instance.</summary>
/// <remarks>Use a durable implementation across restarts when the same keys and titles are reused.</remarks>
public sealed class MemoryInvocationCounterStore : IInvocationCounterStore
{
    private readonly Lock gate = new();
    private readonly Dictionary<string, uint> next = new(StringComparer.Ordinal);
    private readonly uint initialOutgoingCounter;

    /// <summary>Creates a counter store starting at the supplied outgoing value in each new scope.</summary>
    /// <param name="initialOutgoingCounter">The first outgoing counter. Incoming scopes start at zero.</param>
    public MemoryInvocationCounterStore(uint initialOutgoingCounter = 0)
        => this.initialOutgoingCounter = initialOutgoingCounter;

    /// <inheritdoc />
    public uint Reserve(string scope)
    {
        lock (gate)
        {
            uint counter = next.GetValueOrDefault(scope, initialOutgoingCounter);
            if (counter == uint.MaxValue)
            {
                throw new CryptographicException("The invocation counter is exhausted.");
            }

            next[scope] = counter + 1;
            return counter;
        }
    }

    /// <inheritdoc />
    public bool TryAccept(string scope, uint counter)
    {
        lock (gate)
        {
            if (counter == uint.MaxValue || counter < next.GetValueOrDefault(scope))
            {
                return false;
            }

            next[scope] = counter + 1;
            return true;
        }
    }
}
