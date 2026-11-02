namespace EMCL.Protocol.Apdu;

/// <summary>Defines wire choices and conformance encoding for supported xDLMS APDUs.</summary>
internal static class ApduEncoding
{
    /// <summary>The normal LN service choice.</summary>
    internal const byte NormalChoice = 1;
    /// <summary>The GET-next request choice.</summary>
    internal const byte GetNextChoice = 2;
    /// <summary>The GET response-with-data-block choice.</summary>
    internal const byte GetBlockChoice = 2;
    internal const byte SetFirstBlockChoice = 2;
    internal const byte SetBlockChoice = 3;
    internal const byte SetBlockResponseChoice = 2;
    internal const byte SetLastBlockResponseChoice = 3;
    /// <summary>The data alternative of get-data-result.</summary>
    internal const byte DataResult = 0;
    /// <summary>The error alternative of get-data-result.</summary>
    internal const byte ErrorResult = 1;
    /// <summary>The first octet of the conformance application tag.</summary>
    internal const byte ConformanceTagFirst = 0x5f;
    /// <summary>The last octet of the conformance application tag.</summary>
    internal const byte ConformanceTagLast = 0x1f;
    /// <summary>The encoded conformance bit-string content length.</summary>
    internal const int ConformanceLength = 4;
    /// <summary>The unused-bit count for the 24-bit conformance value.</summary>
    internal const byte UnusedConformanceBits = 0;
    /// <summary>The largest 24-bit conformance mask.</summary>
    internal const uint MaximumConformance = 0xffffff;
}
