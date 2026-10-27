using System.Buffers;

namespace EMCL.Core.Codecs;

/// <summary>Adds allocation-based convenience methods to incremental codecs.</summary>
public static class CodecExtensions
{
    /// <summary>Encodes a value into a newly allocated byte array owned by the caller.</summary>
    /// <param name="codec">The codec used to encode or decode complete values.</param>
    /// <param name="value">The value to encode into a newly allocated array.</param>
    /// <typeparam name="T">The decoded or encoded value type.</typeparam>
    /// <returns>A new array containing exactly one encoded value.</returns>
    public static byte[] EncodeBytes<T>(this IIncrementalCodec<T> codec, T value)
    {
        var writer = new ArrayBufferWriter<byte>();
        codec.Encode(value, writer);
        return writer.WrittenSpan.ToArray();
    }
}
