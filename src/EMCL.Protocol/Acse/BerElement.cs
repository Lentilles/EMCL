using System.Buffers;
using System.Collections.Immutable;

namespace EMCL.Protocol.Acse;

/// <summary>Preserves a single-octet BER tag and its content octets, excluding the tag and length encoding.</summary>
/// <param name="Tag">The single-octet BER tag.</param>
/// <param name="Content">The field content without its tag or length octets.</param>
public sealed record BerElement(byte Tag, ImmutableArray<byte> Content)
{
    /// <summary>Copies raw content octets into a primitive or explicitly constructed BER field.</summary>
    /// <param name="tag">The single-octet BER field tag.</param>
    /// <param name="content">The raw content bytes to copy.</param>
    public static BerElement Create(byte tag, params byte[] content) => new(tag, content.ToImmutableArray());
    /// <summary>Encodes child BER fields into the content of a constructed field.</summary>
    /// <param name="tag">The single-octet BER field tag.</param>
    /// <param name="fields">The BER children to encode as content.</param>
    public static BerElement Constructed(byte tag, params BerElement[] fields)
    {
        var w = new ArrayBufferWriter<byte>();
        foreach (var field in fields)
        {
            AcseCodec.WriteField(field, w);
        }

        return new(tag, w.WrittenSpan.ToArray().ToImmutableArray());
    }
}
