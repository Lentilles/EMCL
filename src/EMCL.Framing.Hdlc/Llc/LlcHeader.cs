namespace EMCL.Framing.Hdlc.Llc;

/// <summary>Defines the DLMS LLC header carried by reassembled HDLC information.</summary>
public static class LlcHeader
{
    /// <summary>The LLC header length in octets.</summary>
    public const int Length = 3;
    /// <summary>The DLMS destination LSAP.</summary>
    public const byte DlmsDestination = 0xe6;
    /// <summary>The broadcast destination LSAP.</summary>
    public const byte BroadcastDestination = 0xff;
    /// <summary>The command source LSAP.</summary>
    public const byte CommandSource = 0xe6;
    /// <summary>The response source LSAP.</summary>
    public const byte ResponseSource = 0xe7;
    /// <summary>The DLMS LLC quality octet.</summary>
    public const byte Quality = 0;
}
