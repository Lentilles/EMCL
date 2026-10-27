namespace EMCL.Core.Access;

/// <summary>Defines Association LN v3 access and protection flags while preserving raw permission bytes.</summary>
[Flags]
public enum AccessModeV3 : byte
{
    /// <summary>No access or protection flags.</summary>
    None = 0,
    /// <summary>Attribute read access.</summary>
    Read = 1,
    /// <summary>Method invocation access.</summary>
    Invoke = 1,
    /// <summary>Attribute write access.</summary>
    Write = 2,
    /// <summary>An authenticated request is required.</summary>
    AuthenticatedRequest = 4,
    /// <summary>An encrypted request is required.</summary>
    EncryptedRequest = 8,
    /// <summary>A digitally signed request is required.</summary>
    SignedRequest = 16,
    /// <summary>An authenticated response is required.</summary>
    AuthenticatedResponse = 32,
    /// <summary>An encrypted response is required.</summary>
    EncryptedResponse = 64,
    /// <summary>A digitally signed response is required.</summary>
    SignedResponse = 128
}
