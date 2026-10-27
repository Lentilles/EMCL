using System.Buffers;
using System.Collections.Immutable;
using EMCL.Core.Codecs;

namespace EMCL.Framing.Hdlc.Llc;

/// <summary>Encodes and decodes the DLMS LLC header around one already delimited application payload.</summary>
/// <remarks>The input must already contain exactly one reassembled information message. LLC itself has no payload-length field.</remarks>
/// <param name="protocolLimits">Optional resource bounds; null selects validated defaults.</param>
public sealed class LlcCodec(ProtocolLimits? protocolLimits = null) : IIncrementalCodec<LlcMessage>
{
    private readonly ProtocolLimits limits = ProtocolLimits.Resolve(protocolLimits);
    /// <inheritdoc />
    public DecodeResult<LlcMessage> Decode(ReadOnlySequence<byte> input)
    {
        if (input.Length < LlcHeader.Length)
        {
            return new(DecodeStatus.NeedMoreData);
        }

        if (input.Length - LlcHeader.Length > limits.MaxMessageBytes)
        {
            return new(DecodeStatus.InvalidData, Error: "LLC payload limit exceeded.");
        }

        Span<byte> header = stackalloc byte[LlcHeader.Length];
        input.Slice(0, LlcHeader.Length).CopyTo(header);
        if (header[0] is not (LlcHeader.DlmsDestination or LlcHeader.BroadcastDestination) || header[1] is not (LlcHeader.CommandSource or LlcHeader.ResponseSource) || header[2] != LlcHeader.Quality)
        {
            return new(DecodeStatus.InvalidData, Error: "Invalid LLC header.");
        }

        return DecodeResult<LlcMessage>.Done(new(header[1] == LlcHeader.ResponseSource, input.Slice(LlcHeader.Length).ToArray().ToImmutableArray(), header[0] == LlcHeader.BroadcastDestination), input.Length);
    }

    /// <inheritdoc />
    public void Encode(LlcMessage value, IBufferWriter<byte> output)
    {
        if (value.Apdu.Length > limits.MaxMessageBytes)
        {
            throw new ArgumentException("LLC payload limit exceeded.");
        }

        var span = output.GetSpan(LlcHeader.Length + value.Apdu.Length);
        span[0] = value.Broadcast ? LlcHeader.BroadcastDestination : LlcHeader.DlmsDestination;
        span[1] = value.Response ? LlcHeader.ResponseSource : LlcHeader.CommandSource;
        span[2] = LlcHeader.Quality;
        value.Apdu.AsSpan().CopyTo(span[LlcHeader.Length..]);
        output.Advance(LlcHeader.Length + value.Apdu.Length);
    }
}
