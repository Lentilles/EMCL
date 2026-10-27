namespace EMCL.Framing.Wrapper;

/// <summary>Defines the Wrapper version-1 header layout.</summary>
public static class WrapperHeader
{
    /// <summary>The supported Wrapper version.</summary>
    public const ushort Version = 1;
    /// <summary>The complete header length in octets.</summary>
    public const int Length = 4 * sizeof(ushort);
    /// <summary>The source wPort offset.</summary>
    public const int SourcePortOffset = sizeof(ushort);
    /// <summary>The destination wPort offset.</summary>
    public const int DestinationPortOffset = 2 * sizeof(ushort);
    /// <summary>The payload-length field offset.</summary>
    public const int PayloadLengthOffset = 3 * sizeof(ushort);
}
