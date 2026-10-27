namespace EMCL.Client.Exceptions;

/// <summary>Reports a malformed, unexpected, or uncorrelated message that invalidates a client association.</summary>
public class DlmsProtocolException : IOException
{
    /// <summary>Creates a protocol failure with a diagnostic that contains no authentication secrets.</summary>
    /// <param name="message">The failure description.</param>
    public DlmsProtocolException(string message) : base(message) { }
}
