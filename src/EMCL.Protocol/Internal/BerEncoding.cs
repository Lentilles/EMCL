namespace EMCL.Protocol.Internal;

/// <summary>Defines single-octet BER tags and definite-length encoding masks.</summary>
internal static class BerEncoding
{
    /// <summary>The universal INTEGER tag.</summary>
    internal const byte IntegerTag = 2;
    /// <summary>The universal OCTET STRING tag.</summary>
    internal const byte OctetStringTag = 4;
    /// <summary>The universal OBJECT IDENTIFIER tag.</summary>
    internal const byte ObjectIdentifierTag = 6;
    /// <summary>The constructed encoding bit.</summary>
    internal const byte ConstructedMask = 0x20;
    /// <summary>The high-tag-number sentinel mask.</summary>
    internal const byte HighTagNumberMask = 0x1f;
    /// <summary>The long-form length flag.</summary>
    internal const byte LongLengthMask = 0x80;
    /// <summary>The long-form length octet-count mask.</summary>
    internal const byte LengthOctetCountMask = 0x7f;
    /// <summary>The maximum supported definite-length octet count.</summary>
    internal const int MaximumLengthOctets = sizeof(uint);
    /// <summary>The largest length that occupies three octets.</summary>
    internal const int MaximumThreeOctetLength = 0xffffff;
}
