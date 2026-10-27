namespace EMCL.Core.Codecs;

/// <summary>Indicates whether decoding completed, requires more input, or cannot accept the input.</summary>
public enum DecodeStatus
{
    /// <summary>The first value was decoded successfully; Consumed identifies its byte boundary.</summary>
    Done,
    /// <summary>The input is a valid prefix of a supported value and more bytes are required.</summary>
    NeedMoreData,
    /// <summary>The input violates the wire format or configured resource limits.</summary>
    InvalidData,
    /// <summary>The tag, version, or protocol variant is recognized as unsupported.</summary>
    Unsupported
}
