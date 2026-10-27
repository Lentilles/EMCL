using System.Buffers.Binary;
using System.Buffers;
using System.Collections.Immutable;
using EMCL.Core.Codecs;

namespace EMCL.Framing.Wrapper;

/// <summary>Encodes and incrementally decodes DLMS Wrapper version 1 messages.</summary>
/// <param name="protocolLimits">Optional resource bounds; null selects validated defaults.</param>
public sealed class WrapperCodec(ProtocolLimits? protocolLimits = null) : IIncrementalCodec<WrapperFrame>
{
    private readonly ProtocolLimits limits = ProtocolLimits.Resolve(protocolLimits);
    /// <inheritdoc />
    public DecodeResult<WrapperFrame> Decode(ReadOnlySequence<byte> input)
    {
        if (input.Length < WrapperHeader.Length)
        {
            return new(DecodeStatus.NeedMoreData);
        }

        Span<byte> header = stackalloc byte[WrapperHeader.Length];
        input.Slice(0, WrapperHeader.Length).CopyTo(header);
        ushort version = BinaryPrimitives.ReadUInt16BigEndian(header);
        if (version != WrapperHeader.Version)
        {
            return new(DecodeStatus.Unsupported, Error: $"Unsupported Wrapper version {version}.");
        }

        ushort length = BinaryPrimitives.ReadUInt16BigEndian(header[WrapperHeader.PayloadLengthOffset..]);
        if (length > limits.MaxMessageBytes)
        {
            return new(DecodeStatus.InvalidData, Error: "Wrapper payload limit exceeded.");
        }

        if (input.Length < length + WrapperHeader.Length)
        {
            return new(DecodeStatus.NeedMoreData);
        }

        return DecodeResult<WrapperFrame>.Done(
            new(BinaryPrimitives.ReadUInt16BigEndian(header[WrapperHeader.SourcePortOffset..]), BinaryPrimitives.ReadUInt16BigEndian(header[WrapperHeader.DestinationPortOffset..]), input.Slice(WrapperHeader.Length, length).ToArray().ToImmutableArray()),
            length + WrapperHeader.Length
        );
    }

    /// <inheritdoc />
    public void Encode(WrapperFrame value, IBufferWriter<byte> output)
    {
        if (value.Version != WrapperHeader.Version)
        {
            throw new NotSupportedException("Wrapper version must be one.");
        }

        if (value.Payload.Length > ushort.MaxValue || value.Payload.Length > limits.MaxMessageBytes)
        {
            throw new ArgumentException("Wrapper payload too large.");
        }

        var span = output.GetSpan(WrapperHeader.Length + value.Payload.Length);
        BinaryPrimitives.WriteUInt16BigEndian(span, WrapperHeader.Version);
        BinaryPrimitives.WriteUInt16BigEndian(span[WrapperHeader.SourcePortOffset..], value.SourcePort);
        BinaryPrimitives.WriteUInt16BigEndian(span[WrapperHeader.DestinationPortOffset..], value.DestinationPort);
        BinaryPrimitives.WriteUInt16BigEndian(span[WrapperHeader.PayloadLengthOffset..], (ushort)value.Payload.Length);
        value.Payload.AsSpan().CopyTo(span[WrapperHeader.Length..]);
        output.Advance(WrapperHeader.Length + value.Payload.Length);
    }
}
