namespace EMCL.Client.Association;

/// <summary>Defines authentication mechanisms implemented by the standard LN association negotiator.</summary>
public enum ClientAuthentication : byte
{
    /// <summary>Authentication fields are omitted.</summary>
    None = 0,
    /// <summary>Low-level authentication sends the supplied password without encryption.</summary>
    Low = 1,
    /// <summary>High-level authentication using the Gurux-compatible AES-128 challenge exchange.</summary>
    High = 2
}
