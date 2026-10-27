namespace EMCL.Protocol.Apdu;

/// <summary>Defines the supported LN conformance flags in the encoded 24-bit conformance value.</summary>
[Flags]
public enum DlmsConformance : uint
{
    /// <summary>No services are negotiated.</summary>
    None = 0,
    /// <summary>GET responses may use service-specific block transfer.</summary>
    BlockTransferWithGet = 0x001000,
    /// <summary>The server supports priority management.</summary>
    PriorityManagement = 0x004000,
    /// <summary>The server may send DataNotification APDUs.</summary>
    DataNotification = 0x000080,
    /// <summary>Normal GET requests are supported.</summary>
    Get = 0x000010,
    /// <summary>Normal SET requests are supported.</summary>
    Set = 0x000008,
    /// <summary>Attribute operations may carry selective-access parameters.</summary>
    SelectiveAccess = 0x000004,
    /// <summary>Normal ACTION requests are supported.</summary>
    Action = 0x000001
}
