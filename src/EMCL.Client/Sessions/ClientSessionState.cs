namespace EMCL.Client.Sessions;

/// <summary>Defines the lifecycle of a single-use client association.</summary>
public enum ClientSessionState
{
    /// <summary>No association has been attempted.</summary>
    Created,
    /// <summary>The client is waiting for an association response.</summary>
    Associating,
    /// <summary>The association accepts operations.</summary>
    Associated,
    /// <summary>New operations are refused while existing operations finish and release is exchanged.</summary>
    Releasing,
    /// <summary>The peer confirmed release; create another session for another association.</summary>
    Released,
    /// <summary>The association cannot safely continue after an exchange failure.</summary>
    Faulted,
    /// <summary>The session has been disposed.</summary>
    Disposed
}
