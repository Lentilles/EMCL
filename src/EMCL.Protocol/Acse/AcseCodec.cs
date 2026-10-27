using System.Buffers;
using System.Collections.Immutable;
using EMCL.Core.Codecs;
using EMCL.Protocol.Internal;

namespace EMCL.Protocol.Acse;

/// <summary>Encodes and incrementally decodes definite-length BER ACSE envelopes without performing authentication.</summary>
/// <remarks>Only definite-length BER with single-octet field tags is supported. Unknown supported-form fields retain their order and content.</remarks>
public sealed class AcseCodec : IIncrementalCodec<AcseApdu>
{
    private readonly ProtocolLimits limits;
    /// <summary>Creates a codec using the supplied limits or validated default limits.</summary>
    /// <param name="limits">Optional resource bounds; null selects validated defaults.</param>
    public AcseCodec(ProtocolLimits? limits = null)
    {
        this.limits = ProtocolLimits.Resolve(limits);
    }

    /// <inheritdoc />
    public DecodeResult<AcseApdu> Decode(ReadOnlySequence<byte> input)
    {
        var r = new WireReader(input.Slice(0, Math.Min(input.Length, (long)limits.MaxMessageBytes + 1)).ToArray());
        bool completeEnvelope = false;
        try
        {
            var kind = (AcseKind)r.Byte();
            if (!Enum.IsDefined(kind))
            {
                return new(DecodeStatus.Unsupported, Error: "Not an ACSE APDU.");
            }

            int length = r.Length(limits.MaxMessageBytes);
            var body = new WireReader(r.Take(length));
            completeEnvelope = true;
            var fields = ImmutableArray.CreateBuilder<BerElement>();
            while (body.Remaining > 0)
            {
                if (fields.Count >= limits.MaxCollectionItems)
                {
                    throw new WireException(DecodeStatus.InvalidData, "Too many BER fields.");
                }

                byte tag = body.Byte();
                if ((tag & BerEncoding.HighTagNumberMask) == BerEncoding.HighTagNumberMask)
                {
                    throw new WireException(DecodeStatus.Unsupported, "High-tag-number BER fields are unsupported.");
                }

                int fieldLength = body.Length(limits.MaxMessageBytes);
                if (fieldLength > body.Remaining)
                {
                    throw new WireException(DecodeStatus.InvalidData, "BER field exceeds its envelope.");
                }

                var field = new BerElement(tag, body.Take(fieldLength).ToArray().ToImmutableArray());
                ValidateConstructed(field, 1);
                fields.Add(field);
            }

            if (r.Position > limits.MaxMessageBytes)
            {
                throw new WireException(DecodeStatus.InvalidData, "ACSE limit exceeded.");
            }

            var apdu = new AcseApdu(kind, fields.ToImmutable());
            ValidateEnvelope(apdu);
            return DecodeResult<AcseApdu>.Done(apdu, r.Position);
        }
        catch (WireException e)
        {
            return new(e.Status == DecodeStatus.NeedMoreData && (completeEnvelope || input.Length > limits.MaxMessageBytes) ? DecodeStatus.InvalidData : e.Status, Error: e.Message);
        }
    }

    private static void ValidateEnvelope(AcseApdu apdu)
    {
        if (apdu.Fields.Select(f => f.Tag).Distinct().Count() != apdu.Fields.Length)
        {
            throw new WireException(DecodeStatus.InvalidData, "Repeated ACSE field.");
        }

        if (apdu.Kind is AcseKind.Aarq or AcseKind.Aare)
        {
            if (apdu.ApplicationContext is not { Length: > 0 })
            {
                throw new WireException(DecodeStatus.InvalidData, "Missing ACSE application-context-name.");
            }

            if (apdu.Kind == AcseKind.Aare)
            {
                var result = apdu.Find(AcseFieldTags.AssociationResult) ?? throw new WireException(DecodeStatus.InvalidData, "Missing association result.");
                if (AcseApdu.Unwrap(result, BerEncoding.IntegerTag).Length != 1)
                {
                    throw new WireException(DecodeStatus.InvalidData, "Invalid association result.");
                }

                var diagnostic = apdu.Find(AcseFieldTags.ResultSourceDiagnostic) ?? throw new WireException(DecodeStatus.InvalidData, "Missing source diagnostic.");
                var inner = new WireReader(diagnostic.Content.AsSpan());
                byte source = inner.Byte();
                if (source is not (AcseFieldTags.DiagnosticServiceUser or AcseFieldTags.DiagnosticServiceProvider))
                {
                    throw new WireException(DecodeStatus.InvalidData, "Invalid diagnostic source.");
                }

                var field = new BerElement(source, inner.Take(inner.Length(diagnostic.Content.Length)).ToArray().ToImmutableArray());
                if (inner.Remaining != 0 || AcseApdu.Unwrap(field, BerEncoding.IntegerTag).Length != 1)
                {
                    throw new WireException(DecodeStatus.InvalidData, "Invalid source diagnostic.");
                }
            }
        }

        _ = apdu.UserInformation;
    }

    private void ValidateConstructed(BerElement field, int depth)
    {
        if (depth > limits.MaxDepth)
        {
            throw new WireException(DecodeStatus.InvalidData, "BER depth limit exceeded.");
        }

        if ((field.Tag & BerEncoding.ConstructedMask) == 0)
        {
            return;
        }

        var r = new WireReader(field.Content.AsSpan());
        int count = 0;
        try
        {
            while (r.Remaining > 0)
            {
                if (++count > limits.MaxCollectionItems)
                {
                    throw new WireException(DecodeStatus.InvalidData, "Too many BER fields.");
                }

                byte tag = r.Byte();
                if ((tag & BerEncoding.HighTagNumberMask) == BerEncoding.HighTagNumberMask)
                {
                    throw new WireException(DecodeStatus.Unsupported, "High-tag-number BER fields are unsupported.");
                }

                var child = new BerElement(tag, r.Take(r.Length(limits.MaxMessageBytes)).ToArray().ToImmutableArray());
                ValidateConstructed(child, depth + 1);
            }
        }
        catch (WireException e) when (e.Status == DecodeStatus.NeedMoreData)
        {
            throw new WireException(DecodeStatus.InvalidData, "Malformed constructed BER field.");
        }
    }

    /// <inheritdoc />
    public void Encode(AcseApdu value, IBufferWriter<byte> output)
    {
        try
        {
            EncodeCore(value, output);
        }
        catch (WireException e)
        {
            throw new ArgumentException(e.Message, nameof(value), e);
        }
    }

    private void EncodeCore(AcseApdu value, IBufferWriter<byte> output)
    {
        if (!Enum.IsDefined(value.Kind))
        {
            throw new ArgumentException("Unknown ACSE kind.");
        }

        ValidateEnvelope(value);
        if (value.Fields.Length > limits.MaxCollectionItems)
        {
            throw new ArgumentException("Too many BER fields.");
        }

        var body = new BoundedBufferWriter(limits.MaxMessageBytes);
        foreach (var field in value.Fields)
        {
            ValidateConstructed(field, 1);
            WriteField(field, body);
        }

        var envelope = new BoundedBufferWriter(limits.MaxMessageBytes);
        WireWriter.Byte(envelope, (byte)value.Kind);
        WireWriter.Length(envelope, body.WrittenCount);
        WireWriter.Bytes(envelope, body.WrittenSpan);
        if (envelope.WrittenCount > limits.MaxMessageBytes)
        {
            throw new ArgumentException("ACSE limit exceeded.");
        }

        WireWriter.Bytes(output, envelope.WrittenSpan);
    }

    internal static void WriteField(BerElement field, IBufferWriter<byte> w)
    {
        if ((field.Tag & BerEncoding.HighTagNumberMask) == BerEncoding.HighTagNumberMask)
        {
            throw new ArgumentException("High-tag-number BER fields are unsupported.");
        }

        WireWriter.Byte(w, field.Tag);
        WireWriter.Length(w, field.Content.Length);
        WireWriter.Bytes(w, field.Content.AsSpan());
    }
}
