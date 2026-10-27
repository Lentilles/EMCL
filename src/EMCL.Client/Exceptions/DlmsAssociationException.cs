using EMCL.Protocol.Acse;

namespace EMCL.Client.Exceptions;

/// <summary>Reports a rejected or incompatible ACSE association or release response.</summary>
public sealed class DlmsAssociationException : DlmsProtocolException
{
    /// <summary>Gets the original response for inspection of result and diagnostic fields.</summary>
    public AcseApdu Response
    {
        get;
    }
    /// <summary>Creates an association failure retaining the received ACSE envelope.</summary>
    /// <param name="message">The reason the context cannot be accepted.</param>
    /// <param name="response">The received envelope; authentication fields must be treated as sensitive by the caller.</param>
    public DlmsAssociationException(string message, AcseApdu response) : base(message)
    {
        ArgumentNullException.ThrowIfNull(response);
        Response = response;
    }
}
