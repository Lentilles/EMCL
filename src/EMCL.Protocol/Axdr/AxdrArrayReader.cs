using EMCL.Core.Codecs;
using EMCL.Core.Values;
using EMCL.Protocol.Internal;

namespace EMCL.Protocol.Axdr;

// The caller retains unread bytes and advances only after a successful decode.
internal sealed class AxdrArrayReader(ProtocolLimits limits)
{
    private readonly AxdrCodec codec = new(limits);
    private CompactTypeDescription? description;
    private int budget = limits.MaxCollectionItems - 1; // Reserve one value for the enclosing array.
    private int rowsRead;
    private int contentBytesRead;

    internal int? TotalRows
    {
        get; private set;
    }
    internal int? TotalContentBytes
    {
        get; private set;
    }
    internal int RowsRead => Volatile.Read(ref rowsRead);
    internal int ContentBytesRead => Volatile.Read(ref contentBytesRead);
    internal bool IsComplete => TotalRows == rowsRead || TotalContentBytes == contentBytesRead;

    internal DecodeResult<DlmsType> ReadHeader(ReadOnlySpan<byte> input)
    {
        var reader = new WireReader(input);
        try
        {
            var type = (DlmsType)reader.Byte();
            switch (type)
            {
                case DlmsType.Array:
                    TotalRows = reader.Length(budget);
                    break;
                case DlmsType.CompactArray:
                    int remainingBudget = budget;
                    var elementType = codec.ReadDescription(ref reader, 1, ref remainingBudget);
                    int contentLength = reader.Length(limits.MaxMessageBytes);
                    if (contentLength > limits.MaxMessageBytes - reader.Position)
                    {
                        throw new WireException(DecodeStatus.InvalidData, "Compact-array exceeds the message limit.");
                    }

                    description = elementType;
                    TotalContentBytes = contentLength;
                    budget = remainingBudget;
                    break;
                default:
                    throw new WireException(DecodeStatus.InvalidData, "Expected an A-XDR array or compact-array.");
            }

            return DecodeResult<DlmsType>.Done(type, reader.Position);
        }
        catch (WireException error)
        {
            return new(error.Status, Error: error.Message);
        }
    }

    internal DecodeResult<DlmsValue> ReadRow(ReadOnlySpan<byte> input)
    {
        int? remainingContent = TotalContentBytes - contentBytesRead;
        if (remainingContent is { } length)
        {
            input = input[..Math.Min(input.Length, length)];
        }

        var reader = new WireReader(input);
        try
        {
            int remainingBudget = budget;
            var row = description is null
                ? codec.Read(ref reader, 1, ref remainingBudget)
                : codec.ReadCompact(ref reader, description, 1, ref remainingBudget);
            if (reader.Position == 0)
            {
                throw new WireException(DecodeStatus.InvalidData, "Zero-width compact row.");
            }

            budget = remainingBudget;
            Volatile.Write(ref contentBytesRead, contentBytesRead + reader.Position);
            Volatile.Write(ref rowsRead, rowsRead + 1);
            return DecodeResult<DlmsValue>.Done(row, reader.Position);
        }
        catch (WireException error)
        {
            if (error.Status == DecodeStatus.NeedMoreData && remainingContent == input.Length)
            {
                return new(DecodeStatus.InvalidData, Error: "Truncated row inside complete compact-array contents.");
            }

            return new(error.Status, Error: error.Message);
        }
        catch (Exception error) when (error is ArgumentException or OverflowException)
        {
            return new(DecodeStatus.InvalidData, Error: error.Message);
        }
    }
}
