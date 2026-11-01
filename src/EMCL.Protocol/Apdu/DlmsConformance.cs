namespace EMCL.Protocol.Apdu;

/// <summary>Defines the service bits in the encoded 24-bit xDLMS conformance value.</summary>
[Flags]
public enum DlmsConformance : uint
{
    /// <summary>No services are negotiated.</summary>
    None = 0,
    /// <summary>Protected xDLMS messages.</summary>
    GeneralProtection = 0x400000,
    /// <summary>General block transfer.</summary>
    GeneralBlockTransfer = 0x200000,
    /// <summary>Short-name READ service.</summary>
    Read = 0x100000,
    /// <summary>Short-name WRITE service.</summary>
    Write = 0x080000,
    /// <summary>Unconfirmed short-name WRITE service.</summary>
    UnconfirmedWrite = 0x040000,
    /// <summary>Delta value encoding.</summary>
    DeltaValueEncoding = 0x020000,
    /// <summary>Attribute 0 in SET requests.</summary>
    Attribute0SupportedWithSet = 0x008000,
    /// <summary>Attribute 0 in GET requests.</summary>
    Attribute0SupportedWithGet = 0x002000,
    /// <summary>GET responses may use service-specific block transfer.</summary>
    BlockTransferWithGet = 0x001000,
    /// <summary>SET service-specific block transfer; recognized in a peer response but not implemented by the client.</summary>
    BlockTransferWithSet = 0x000800,
    /// <summary>ACTION service-specific block transfer.</summary>
    BlockTransferWithAction = 0x000400,
    /// <summary>Multiple object references in one request.</summary>
    MultipleReferences = 0x000200,
    /// <summary>Short-name information reports.</summary>
    InformationReport = 0x000100,
    /// <summary>The server supports priority management.</summary>
    PriorityManagement = 0x004000,
    /// <summary>The server may send DataNotification APDUs.</summary>
    DataNotification = 0x000080,
    /// <summary>ACCESS service.</summary>
    Access = 0x000040,
    /// <summary>Parameterized short-name access.</summary>
    ParameterizedAccess = 0x000020,
    /// <summary>Normal GET requests are supported.</summary>
    Get = 0x000010,
    /// <summary>Normal SET requests are supported.</summary>
    Set = 0x000008,
    /// <summary>Attribute operations may carry selective-access parameters.</summary>
    SelectiveAccess = 0x000004,
    /// <summary>Event notifications.</summary>
    EventNotification = 0x000002,
    /// <summary>Normal ACTION requests are supported.</summary>
    Action = 0x000001
}
