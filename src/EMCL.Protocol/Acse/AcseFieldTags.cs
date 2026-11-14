namespace EMCL.Protocol.Acse;

/// <summary>Defines ACSE field tags within association and release envelopes.</summary>
public static class AcseFieldTags
{
    /// <summary>The application-context-name field.</summary>
    public const byte ApplicationContextName = 0xa1;
    /// <summary>The association-result field.</summary>
    public const byte AssociationResult = 0xa2;
    /// <summary>The result-source-diagnostic field.</summary>
    public const byte ResultSourceDiagnostic = 0xa3;
    /// <summary>The sender ACSE requirements field.</summary>
    public const byte SenderAcseRequirements = 0x8a;
    /// <summary>The authentication mechanism-name field.</summary>
    public const byte MechanismName = 0x8b;
    /// <summary>The calling AP-title field in an AARQ.</summary>
    public const byte CallingApTitle = 0xa6;
    /// <summary>The responding AP-title field in an AARE.</summary>
    public const byte RespondingApTitle = 0xa4;
    /// <summary>The responder ACSE requirements field.</summary>
    public const byte ResponderAcseRequirements = 0x88;
    /// <summary>The responding mechanism-name field in an AARE.</summary>
    public const byte RespondingMechanismName = 0x89;
    /// <summary>The calling-authentication-value field.</summary>
    public const byte CallingAuthenticationValue = 0xac;
    /// <summary>The responding-authentication-value field in an AARE.</summary>
    public const byte RespondingAuthenticationValue = 0xaa;
    /// <summary>The user-information field.</summary>
    public const byte UserInformation = 0xbe;
    /// <summary>The ACSE service-user diagnostic choice.</summary>
    public const byte DiagnosticServiceUser = 0xa1;
    /// <summary>The ACSE service-provider diagnostic choice.</summary>
    public const byte DiagnosticServiceProvider = 0xa2;
    /// <summary>The graphic-string authentication-value choice.</summary>
    public const byte AuthenticationGraphicString = 0x80;
    /// <summary>The release-reason field.</summary>
    public const byte ReleaseReason = 0x80;
}
