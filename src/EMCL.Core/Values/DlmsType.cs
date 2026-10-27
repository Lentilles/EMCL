namespace EMCL.Core.Values;

/// <summary>Defines the A-XDR Data choice tags without conflating types that share a CLR representation.</summary>
public enum DlmsType : byte
{
    /// <summary>The null-data tag.</summary>
    Null = 0,
    /// <summary>An ordered array of tagged values.</summary>
    Array = 1,
    /// <summary>An ordered structure of tagged fields.</summary>
    Structure = 2,
    /// <summary>A Boolean value.</summary>
    Boolean = 3,
    /// <summary>A packed bit string with an explicit bit count.</summary>
    BitString = 4,
    /// <summary>A signed 32-bit integer.</summary>
    Int32 = 5,
    /// <summary>A unsigned 32-bit integer.</summary>
    UInt32 = 6,
    /// <summary>An uninterpreted octet string.</summary>
    OctetString = 9,
    /// <summary>An ASCII visible string.</summary>
    VisibleString = 10,
    /// <summary>A UTF-8 string.</summary>
    Utf8String = 12,
    /// <summary>An octet carrying a binary-coded decimal representation.</summary>
    Bcd = 13,
    /// <summary>A signed 8-bit integer.</summary>
    Int8 = 15,
    /// <summary>A signed 16-bit integer.</summary>
    Int16 = 16,
    /// <summary>A unsigned 8-bit integer.</summary>
    UInt8 = 17,
    /// <summary>A unsigned 16-bit integer.</summary>
    UInt16 = 18,
    /// <summary>Rows encoded using a shared type description.</summary>
    CompactArray = 19,
    /// <summary>A signed 64-bit integer.</summary>
    Int64 = 20,
    /// <summary>A unsigned 64-bit integer.</summary>
    UInt64 = 21,
    /// <summary>An enumerated octet, distinct from an unsigned integer.</summary>
    Enum = 22,
    /// <summary>An IEEE 754 binary32 value.</summary>
    Float32 = 23,
    /// <summary>An IEEE 754 binary64 value.</summary>
    Float64 = 24,
    /// <summary>A twelve-octet COSEM date-time value.</summary>
    DateTime = 25,
    /// <summary>A five-octet COSEM date value.</summary>
    Date = 26,
    /// <summary>A four-octet COSEM time value.</summary>
    Time = 27,
    /// <summary>A signed 8-bit delta value; reconstruction is left to the application.</summary>
    DeltaInt8 = 28,
    /// <summary>A signed 16-bit delta value; reconstruction is left to the application.</summary>
    DeltaInt16 = 29,
    /// <summary>A signed 32-bit delta value; reconstruction is left to the application.</summary>
    DeltaInt32 = 30,
    /// <summary>A unsigned 8-bit delta value; reconstruction is left to the application.</summary>
    DeltaUInt8 = 31,
    /// <summary>A unsigned 16-bit delta value; reconstruction is left to the application.</summary>
    DeltaUInt16 = 32,
    /// <summary>A unsigned 32-bit delta value; reconstruction is left to the application.</summary>
    DeltaUInt32 = 33
}
