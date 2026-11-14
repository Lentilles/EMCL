namespace EMCL.Protocol.Security;

/// <summary>Provides atomic invocation counter updates. Persist these updates when using persistent keys.</summary>
/// <remarks>Scopes are opaque, stable identifiers derived from the suite, system titles and key fingerprints. Reserve values before use.</remarks>
public interface IInvocationCounterStore
{
    /// <summary>Atomically reserves and returns the next counter in the scope, or throws when exhausted.</summary>
    /// <param name="scope">The sender and key scope.</param>
    uint Reserve(string scope);

    /// <summary>Atomically accepts a verified counter and raises the minimum to counter plus one.</summary>
    /// <param name="scope">The sender, recipient and key scope.</param>
    /// <param name="counter">The verified incoming counter.</param>
    /// <returns>False for a replay or an exhausted counter.</returns>
    bool TryAccept(string scope, uint counter);
}
