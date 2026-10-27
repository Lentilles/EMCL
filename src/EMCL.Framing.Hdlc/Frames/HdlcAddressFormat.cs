namespace EMCL.Framing.Hdlc.Frames;

/// <summary>Defines HDLC address-group widths and extension markers.</summary>
internal static class HdlcAddressFormat
{
    /// <summary>The payload-bit count in one address octet.</summary>
    internal const int GroupBits = 7;
    /// <summary>The payload-bit count in an extended server component.</summary>
    internal const int ExtendedComponentBits = 14;
    /// <summary>The seven-bit address payload mask.</summary>
    internal const byte GroupMask = 0x7f;
    /// <summary>The fourteen-bit server component mask.</summary>
    internal const ushort ExtendedComponentMask = 0x3fff;
    /// <summary>The address extension bit marking the final octet.</summary>
    internal const byte LastOctetMask = 0x01;
    /// <summary>The client address length.</summary>
    internal const byte ClientLength = 1;
    /// <summary>The regular compound server address length.</summary>
    internal const byte ServerLength = 2;
    /// <summary>The extended compound server address length.</summary>
    internal const byte ExtendedServerLength = 4;
}
