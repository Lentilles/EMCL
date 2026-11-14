namespace EMCL.Protocol.Apdu;

/// <summary>Identifies supported xDLMS application protocol data units by their wire tag.</summary>
public enum ApduTag : byte
{
    /// <summary>The Initiate-request tag.</summary>
    InitiateRequest = 0x01,
    /// <summary>The Initiate-response tag.</summary>
    InitiateResponse = 0x08,
    /// <summary>The Data-notification tag.</summary>
    DataNotification = 0x0f,
    /// <summary>The Data-notification-confirm tag.</summary>
    DataNotificationConfirm = 0x10,
    /// <summary>The general-block-transfer tag.</summary>
    GeneralBlockTransfer = 0xe0,
    /// <summary>The general-global-ciphering envelope tag.</summary>
    GeneralGlobalCiphering = 0xdb,
    /// <summary>The general-dedicated-ciphering envelope tag.</summary>
    GeneralDedicatedCiphering = 0xdc,
    /// <summary>The GET request tag.</summary>
    GetRequest = 0xc0,
    /// <summary>The SET request tag.</summary>
    SetRequest = 0xc1,
    /// <summary>The ACTION request tag.</summary>
    ActionRequest = 0xc3,
    /// <summary>The GET response tag.</summary>
    GetResponse = 0xc4,
    /// <summary>The SET response tag.</summary>
    SetResponse = 0xc5,
    /// <summary>The ACTION response tag.</summary>
    ActionResponse = 0xc7
}
