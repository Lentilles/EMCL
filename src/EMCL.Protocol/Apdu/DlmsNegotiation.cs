namespace EMCL.Protocol.Apdu;

/// <summary>Defines standard xDLMS negotiation identifiers.</summary>
public static class DlmsNegotiation
{
    /// <summary>The virtual application association name for LN referencing.</summary>
    public const ushort LogicalNameVaa = 7;
    /// <summary>The maximum PDU value denoting no protocol-level size limit.</summary>
    public const ushort UnlimitedPduSize = 0;
    /// <summary>The smallest non-reserved maximum receive PDU size.</summary>
    public const ushort MinimumPduSize = 12;
    /// <summary>DLMS application protocol version six.</summary>
    public const byte Version6 = 6;
}
