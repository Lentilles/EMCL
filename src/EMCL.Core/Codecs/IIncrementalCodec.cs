using System.Buffers;

namespace EMCL.Core.Codecs;

/// <summary>Encodes values and incrementally decodes the first value from a byte sequence without performing I/O.</summary>
/// <remarks>Decode never advances caller-owned state. Callers retain incomplete input and pass the combined bytes again. Encode implementations must not retain the supplied buffer writer.</remarks>
/// <typeparam name="T">The decoded or encoded value type.</typeparam>
public interface IIncrementalCodec<T>
{
    /// <summary>Decodes the first complete value from the supplied sequence without consuming caller-owned input.</summary>
    /// <remarks>Only Done consumes bytes. Other statuses report zero consumption so the caller controls buffering and recovery.</remarks>
    /// <param name="input">The available input bytes, which may contain an incomplete prefix or additional trailing values.</param>
    /// <returns>The decode status, first decoded value, successful byte consumption, and optional diagnostic.</returns>
    DecodeResult<T> Decode(ReadOnlySequence<byte> input);
    /// <summary>Writes the wire representation of a value to the supplied buffer writer.</summary>
    /// <param name="value">The value to encode as exactly one wire representation.</param>
    /// <param name="output">The destination buffer writer.</param>
    void Encode(T value, IBufferWriter<byte> output);
}
