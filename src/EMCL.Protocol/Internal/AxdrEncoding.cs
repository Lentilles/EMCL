namespace EMCL.Protocol.Internal;

/// <summary>Defines A-XDR Boolean and optional-value encodings.</summary>
internal static class AxdrEncoding
{
    /// <summary>A false Boolean or absent optional value.</summary>
    internal const byte False = 0;
    /// <summary>A true Boolean or present optional value.</summary>
    internal const byte True = 1;
}
