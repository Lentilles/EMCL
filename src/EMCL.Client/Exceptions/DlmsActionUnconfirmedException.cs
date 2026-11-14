namespace EMCL.Client.Exceptions;

/// <summary>Reports an ACTION whose final result could not be verified after a send attempt.</summary>
public sealed class DlmsActionUnconfirmedException : IOException
{
    /// <summary>Creates an unconfirmed ACTION outcome.</summary>
    /// <param name="innerException">The error that prevented verification of the result.</param>
    public DlmsActionUnconfirmedException(Exception innerException)
        : base("The ACTION outcome is unconfirmed.", innerException)
    {
        ArgumentNullException.ThrowIfNull(innerException);
    }
}
