namespace EMCL.Client.Exceptions;

/// <summary>Reports a SET send attempt without a validated final result. The value may have been written; no retry is performed.</summary>
/// <remarks>Inspect InnerException for timeout, caller cancellation, transport, or protocol failure. Reconcile the value before retrying on a new session.</remarks>
public sealed class DlmsWriteUnconfirmedException : IOException
{
    /// <summary>Creates an unconfirmed write outcome preserving its original cause.</summary>
    /// <param name="innerException">The failure after a send attempt, including cancellation when applicable.</param>
    public DlmsWriteUnconfirmedException(Exception innerException)
        : base("The SET outcome is unconfirmed. Read the value before retrying the write.", innerException)
    {
        ArgumentNullException.ThrowIfNull(innerException);
    }
}
