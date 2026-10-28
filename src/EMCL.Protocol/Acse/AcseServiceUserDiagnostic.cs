namespace EMCL.Protocol.Acse;

/// <summary>Names standard ACSE service-user association diagnostics.</summary>
public enum AcseServiceUserDiagnostic : byte
{
    /// <summary>No diagnostic.</summary>
    None = 0,
    /// <summary>The server supplied no specific reason.</summary>
    NoReasonGiven = 1,
    /// <summary>The proposed application context is unsupported.</summary>
    ApplicationContextNameNotSupported = 2,
    /// <summary>The calling application title is not recognized.</summary>
    CallingApTitleNotRecognized = 3,
    /// <summary>The calling application invocation identifier is not recognized.</summary>
    CallingApInvocationIdentifierNotRecognized = 4,
    /// <summary>The calling application entity qualifier is not recognized.</summary>
    CallingAeQualifierNotRecognized = 5,
    /// <summary>The calling application entity invocation identifier is not recognized.</summary>
    CallingAeInvocationIdentifierNotRecognized = 6,
    /// <summary>The called application title is not recognized.</summary>
    CalledApTitleNotRecognized = 7,
    /// <summary>The called application invocation identifier is not recognized.</summary>
    CalledApInvocationIdentifierNotRecognized = 8,
    /// <summary>The called application entity qualifier is not recognized.</summary>
    CalledAeQualifierNotRecognized = 9,
    /// <summary>The called application entity invocation identifier is not recognized.</summary>
    CalledAeInvocationIdentifierNotRecognized = 10,
    /// <summary>The authentication mechanism name is not recognized.</summary>
    AuthenticationMechanismNameNotRecognized = 11,
    /// <summary>An authentication mechanism name is required.</summary>
    AuthenticationMechanismNameRequired = 12,
    /// <summary>Authentication failed.</summary>
    AuthenticationFailure = 13,
    /// <summary>Authentication is required.</summary>
    AuthenticationRequired = 14
}
