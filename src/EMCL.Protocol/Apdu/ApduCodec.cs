using System.Buffers;
using System.Collections.Immutable;
using EMCL.Core.Codecs;
using EMCL.Core.Objects;
using EMCL.Core.Operations;
using EMCL.Core.Values;
using EMCL.Protocol.Acse;
using EMCL.Protocol.Axdr;
using EMCL.Protocol.Internal;

namespace EMCL.Protocol.Apdu;

/// <summary>Encodes and incrementally decodes the supported LN services, notifications, negotiation messages, and ACSE envelopes.</summary>
public sealed class ApduCodec : IIncrementalCodec<DlmsApdu>
{
    private readonly ProtocolLimits limits;
    private readonly AxdrCodec axdr;
    private readonly AcseCodec acse;
    /// <summary>Creates a codec using the supplied limits or validated default limits.</summary>
    /// <param name="limits">Optional resource bounds; null selects validated defaults.</param>
    public ApduCodec(ProtocolLimits? limits = null)
    {
        this.limits = ProtocolLimits.Resolve(limits);
        axdr = new(this.limits);
        acse = new(this.limits);
    }

    /// <inheritdoc />
    public DecodeResult<DlmsApdu> Decode(ReadOnlySequence<byte> input)
    {
        var bytes = input.Slice(0, Math.Min(input.Length, (long)limits.MaxMessageBytes + 1)).ToArray();
        if (bytes.Length == 0)
        {
            return new(DecodeStatus.NeedMoreData);
        }

        if (bytes[0] is >= (byte)AcseKind.Aarq and <= (byte)AcseKind.Rlre)
        {
            var result = acse.Decode(input);
            return new(result.Status, result.Value, result.Consumed, result.Error);
        }

        var reader = new WireReader(bytes);
        try
        {
            int budget = limits.MaxCollectionItems;
            var tag = (ApduTag)reader.Byte();
            DlmsApdu value = tag switch
            {
                ApduTag.GetRequest or ApduTag.SetRequest or ApduTag.ActionRequest => ReadRequest(tag, ref reader, ref budget),
                ApduTag.GetResponse or ApduTag.SetResponse or ApduTag.ActionResponse => ReadResponse(tag, ref reader, ref budget),
                ApduTag.DataNotification => ReadNotification(ref reader, ref budget),
                ApduTag.DataNotificationConfirm => new DataNotificationConfirm(reader.U32(), ReadNotificationTime(ref reader)),
                ApduTag.GeneralBlockTransfer => ReadGeneralBlock(ref reader),
                ApduTag.InitiateRequest => ReadInitiateRequest(ref reader),
                ApduTag.InitiateResponse => ReadInitiateResponse(ref reader),
                _ => throw new WireException(DecodeStatus.Unsupported, $"Unsupported APDU tag {(byte)tag:X2}.")
            };
            return Finish(value, reader.Position);
        }
        catch (WireException e)
        {
            return new(e.Status == DecodeStatus.NeedMoreData && input.Length > limits.MaxMessageBytes ? DecodeStatus.InvalidData : e.Status, Error: e.Message);
        }
        catch (Exception e) when (e is ArgumentException or OverflowException)
        {
            return new(DecodeStatus.InvalidData, Error: e.Message);
        }
    }

    private DecodeResult<DlmsApdu> Finish(DlmsApdu value, int count) => count <= limits.MaxMessageBytes ? DecodeResult<DlmsApdu>.Done(value, count) : new(DecodeStatus.InvalidData, Error: "APDU limit exceeded.");
    private OperationResult ReadResult(ref WireReader r, ref int budget) => r.Byte() switch
    {
        ApduEncoding.DataResult => OperationResult.Ok(axdr.Read(ref r, 0, ref budget)),
        ApduEncoding.ErrorResult => new((CosemResultCode)r.Byte()),
        _ => throw new WireException(DecodeStatus.InvalidData, "Invalid get-data-result choice.")
    };
    private static bool Presence(ref WireReader r) => r.Byte() switch
    {
        AxdrEncoding.False => false,
        AxdrEncoding.True => true,
        _ => throw new WireException(DecodeStatus.InvalidData, "Invalid presence/boolean.")
    };
    private static uint ReadConformance(ref WireReader r)
    {
        if (r.Byte() != ApduEncoding.ConformanceTagFirst || r.Byte() != ApduEncoding.ConformanceTagLast || r.Length(ApduEncoding.ConformanceLength) != ApduEncoding.ConformanceLength || r.Byte() != ApduEncoding.UnusedConformanceBits)
        {
            throw new WireException(DecodeStatus.InvalidData, "Invalid conformance bit-string.");
        }

        return (uint)(r.Byte() << 16 | r.Byte() << 8 | r.Byte());
    }

    /// <inheritdoc />
    public void Encode(DlmsApdu value, IBufferWriter<byte> output)
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

    private void EncodeCore(DlmsApdu value, IBufferWriter<byte> output)
    {
        if (value is AcseApdu association)
        {
            acse.Encode(association, output);
            return;
        }

        var writer = new BoundedBufferWriter(limits.MaxMessageBytes);
        int budget = limits.MaxCollectionItems;
        switch (value)
        {
            case LnRequest request:
                WriteLnRequest(request, writer, ref budget);
                break;
            case GetNextRequest next:
                WriteGetNextRequest(next, writer);
                break;
            case SetFirstBlockRequest first:
                if (first.Operation.Kind != CosemOperationKind.Write || first.Operation.Value != null)
                {
                    throw new ArgumentException("A first SET block requires a write descriptor without a value.");
                }

                WriteLnRequest(new(first.InvokeIdAndPriority, first.Operation), writer, ref budget,
                    ApduEncoding.SetFirstBlockChoice);
                WriteSetBlock(first.LastBlock, first.BlockNumber, first.Data, writer);
                break;
            case SetBlockRequest block:
                WireWriter.Byte(writer, (byte)ApduTag.SetRequest);
                WireWriter.Byte(writer, ApduEncoding.SetBlockChoice);
                WireWriter.Byte(writer, block.InvokeIdAndPriority);
                WriteSetBlock(block.LastBlock, block.BlockNumber, block.Data, writer);
                break;
            case SetBlockResponse block:
                WireWriter.Byte(writer, (byte)ApduTag.SetResponse);
                WireWriter.Byte(writer, block.Result.HasValue ? ApduEncoding.SetLastBlockResponseChoice : ApduEncoding.SetBlockResponseChoice);
                WireWriter.Byte(writer, block.InvokeIdAndPriority);
                if (block.Result.HasValue)
                {
                    WireWriter.Byte(writer, (byte)block.Result.Value);
                }

                WireWriter.U32(writer, block.BlockNumber);
                break;
            case ActionBlock block:
                WriteActionBlock(block, writer);
                break;
            case ActionNextBlock next:
                WireWriter.Byte(writer, (byte)(next.Response ? ApduTag.ActionResponse : ApduTag.ActionRequest));
                WireWriter.Byte(writer, next.Response ? ApduEncoding.ActionResponseNextBlock : ApduEncoding.ActionRequestNextBlock);
                WireWriter.Byte(writer, next.InvokeIdAndPriority);
                WireWriter.U32(writer, next.BlockNumber);
                break;
            case LnResponse response:
                WriteLnResponse(response, writer, ref budget);
                break;
            case GetBlockResponse block:
                WriteGetBlockResponse(block, writer);
                break;
            case DataNotification notification:
                WriteDataNotification(notification, writer, ref budget);
                break;
            case DataNotificationConfirm confirmation:
                WriteNotificationHeader(ApduTag.DataNotificationConfirm, confirmation.LongInvokeIdAndPriority, confirmation.DateTime, writer);
                break;
            case GeneralBlockTransfer block:
                WriteGeneralBlock(block, writer);
                break;
            case InitiateRequest initiate:
                WriteInitiateRequest(initiate, writer);
                break;
            case InitiateResponse initiate:
                WriteInitiateResponse(initiate, writer);
                break;
            default:
                throw new NotSupportedException($"Unsupported APDU {value.GetType().Name}.");
        }

        if (writer.WrittenCount > limits.MaxMessageBytes)
        {
            throw new ArgumentException("APDU limit exceeded.");
        }

        WireWriter.Bytes(output, writer.WrittenSpan);
    }

    private void WriteResult(OperationResult result, IBufferWriter<byte> w, ref int budget)
    {
        WireWriter.Byte(w, result.Error == CosemResultCode.Success ? ApduEncoding.DataResult : ApduEncoding.ErrorResult);
        if (result.Error == CosemResultCode.Success)
        {
            axdr.Write(result.Value ?? throw new ArgumentException("GET success requires data."), w, 0, ref budget);
        }
        else
        {
            WireWriter.Byte(w, (byte)result.Error);
        }
    }

    private static void WriteQos(sbyte? qos, IBufferWriter<byte> w)
    {
        WireWriter.Byte(w, qos.HasValue ? AxdrEncoding.True : AxdrEncoding.False);
        if (qos.HasValue)
        {
            WireWriter.Byte(w, (byte)qos.Value);
        }
    }

    private static void WriteConformance(uint conformance, IBufferWriter<byte> w)
    {
        if (conformance > ApduEncoding.MaximumConformance)
        {
            throw new ArgumentOutOfRangeException(nameof(conformance));
        }

        WireWriter.Bytes(w, [ApduEncoding.ConformanceTagFirst, ApduEncoding.ConformanceTagLast, ApduEncoding.ConformanceLength, ApduEncoding.UnusedConformanceBits, (byte)(conformance >> 16), (byte)(conformance >> 8), (byte)conformance]);
    }

    private DlmsApdu ReadRequest(ApduTag tag, ref WireReader reader, ref int budget)
    {
        byte choice = reader.Byte(), invoke = reader.Byte();
        if (tag == ApduTag.ActionRequest && choice == ApduEncoding.ActionRequestNextBlock)
        {
            return new ActionNextBlock(invoke, false, reader.U32());
        }

        if (tag == ApduTag.ActionRequest && choice is ApduEncoding.ActionFirstBlock or ApduEncoding.ActionRequestBlock)
        {
            CosemOperation? method = choice == ApduEncoding.ActionFirstBlock
                ? new(CosemOperationKind.Invoke, reader.U16(), LogicalName.FromBytes(reader.Take(LogicalName.EncodedLength)), (sbyte)reader.Byte())
                : null;
            return ReadActionBlock(invoke, false, method, ref reader);
        }

        if (tag == ApduTag.GetRequest && choice == ApduEncoding.GetNextChoice)
        {
            return new GetNextRequest(invoke, reader.U32());
        }
        else if (tag == ApduTag.SetRequest && choice == ApduEncoding.SetBlockChoice)
        {
            bool last = Presence(ref reader);
            uint number = reader.U32();
            return new SetBlockRequest(invoke, last, number,
                reader.Take(reader.Length(limits.MaxMessageBytes)).ToArray().ToImmutableArray());
        }
        else
        {
            bool firstSetBlock = tag == ApduTag.SetRequest && choice == ApduEncoding.SetFirstBlockChoice;
            if (choice != ApduEncoding.NormalChoice && !firstSetBlock)
            {
                throw new WireException(DecodeStatus.Unsupported, "Only normal LN requests are supported.");
            }

            ushort classId = reader.U16();
            var name = LogicalName.FromBytes(reader.Take(LogicalName.EncodedLength));
            sbyte index = (sbyte)reader.Byte();
            SelectiveAccess? selection = null;
            DlmsValue? data = null;
            var kind = tag == ApduTag.GetRequest ? CosemOperationKind.Read : tag == ApduTag.SetRequest ? CosemOperationKind.Write : CosemOperationKind.Invoke;
            if (kind != CosemOperationKind.Invoke)
            {
                if (Presence(ref reader))
                {
                    selection = new(reader.Byte(), axdr.Read(ref reader, 0, ref budget));
                }

                if (firstSetBlock)
                {
                    bool last = Presence(ref reader);
                    uint number = reader.U32();
                    return new SetFirstBlockRequest(invoke, new(kind, classId, name, index, Selection: selection),
                        last, number, reader.Take(reader.Length(limits.MaxMessageBytes)).ToArray().ToImmutableArray());
                }

                if (kind == CosemOperationKind.Write)
                {
                    data = axdr.Read(ref reader, 0, ref budget);
                }
            }
            else if (Presence(ref reader))
            {
                data = axdr.Read(ref reader, 0, ref budget);
            }

            return new LnRequest(invoke, new(kind, classId, name, index, data, selection));
        }
    }

    private DlmsApdu ReadResponse(ApduTag tag, ref WireReader reader, ref int budget)
    {
        byte choice = reader.Byte(), invoke = reader.Byte();
        if (tag == ApduTag.ActionResponse && choice == ApduEncoding.ActionResponseNextBlock)
        {
            return new ActionNextBlock(invoke, true, reader.U32());
        }

        if (tag == ApduTag.ActionResponse && choice == ApduEncoding.ActionResponseBlock)
        {
            return ReadActionBlock(invoke, true, null, ref reader);
        }

        if (tag == ApduTag.SetResponse && choice is ApduEncoding.SetBlockResponseChoice or ApduEncoding.SetLastBlockResponseChoice)
        {
            CosemResultCode? result = choice == ApduEncoding.SetLastBlockResponseChoice ? (CosemResultCode)reader.Byte() : null;
            return new SetBlockResponse(invoke, reader.U32(), result);
        }

        if (tag == ApduTag.GetResponse && choice == ApduEncoding.GetBlockChoice)
        {
            bool last = Presence(ref reader);
            uint number = reader.U32();
            byte blockChoice = reader.Byte();
            return blockChoice switch
            {
                ApduEncoding.DataResult => new GetBlockResponse(invoke, last, number, reader.Take(reader.Length(limits.MaxMessageBytes)).ToArray().ToImmutableArray()),
                ApduEncoding.ErrorResult => new GetBlockResponse(invoke, last, number, [], (CosemResultCode)reader.Byte()),
                _ => throw new WireException(DecodeStatus.InvalidData, "Invalid data block choice.")
            };
        }
        else
        {
            if (choice != ApduEncoding.NormalChoice)
            {
                throw new WireException(DecodeStatus.Unsupported, "Unsupported LN response variant.");
            }

            CosemOperationKind kind = tag == ApduTag.GetResponse ? CosemOperationKind.Read : tag == ApduTag.SetResponse ? CosemOperationKind.Write : CosemOperationKind.Invoke;
            OperationResult result;
            if (kind == CosemOperationKind.Read)
            {
                result = ReadResult(ref reader, ref budget);
            }
            else
            {
                var error = (CosemResultCode)reader.Byte();
                DlmsValue? data = null;
                if (kind == CosemOperationKind.Invoke && Presence(ref reader))
                {
                    var returned = ReadResult(ref reader, ref budget);
                    result = new(error, returned.Value);
                    return new LnResponse(kind, invoke, result, returned.Error == CosemResultCode.Success ? null : returned.Error);
                }

                result = new(error, data);
            }

            return new LnResponse(kind, invoke, result);
        }
    }

    private DlmsApdu ReadNotification(ref WireReader reader, ref int budget)
    {
        uint invoke = reader.U32();
        var dateTime = ReadNotificationTime(ref reader);
        return new DataNotification(invoke, dateTime, axdr.Read(ref reader, 0, ref budget));
    }

    private static ImmutableArray<byte> ReadNotificationTime(ref WireReader reader)
    {
        int count = reader.Length(DlmsValueSizes.DateTime);
        if (count is not (0 or DlmsValueSizes.DateTime))
        {
            throw new WireException(DecodeStatus.InvalidData, "Notification date-time must be absent or 12 octets.");
        }

        return reader.Take(count).ToArray().ToImmutableArray();
    }

    private DlmsApdu ReadInitiateRequest(ref WireReader reader)
    {
        var key = Presence(ref reader) ? reader.Take(reader.Length(limits.MaxMessageBytes)).ToArray().ToImmutableArray() : ImmutableArray<byte>.Empty;
        bool? response = Presence(ref reader) ? Presence(ref reader) : null;
        sbyte? qos = Presence(ref reader) ? (sbyte)reader.Byte() : null;
        byte version = reader.Byte();
        uint conformance = ReadConformance(ref reader);
        return new InitiateRequest(key, response, qos, version, conformance, reader.U16());
    }

    private static DlmsApdu ReadInitiateResponse(ref WireReader reader)
    {
        sbyte? qos = Presence(ref reader) ? (sbyte)reader.Byte() : null;
        byte version = reader.Byte();
        uint conformance = ReadConformance(ref reader);
        return new InitiateResponse(qos, version, conformance, reader.U16(), reader.U16());
    }

    private void WriteLnRequest(LnRequest request, IBufferWriter<byte> writer, ref int budget,
        byte choice = ApduEncoding.NormalChoice)
    {
        var operation = request.Operation;
        WireWriter.Byte(writer, operation.Kind == CosemOperationKind.Read ? (byte)ApduTag.GetRequest : operation.Kind == CosemOperationKind.Write ? (byte)ApduTag.SetRequest : (byte)ApduTag.ActionRequest);
        WireWriter.Byte(writer, choice);
        WireWriter.Byte(writer, request.InvokeIdAndPriority);
        WireWriter.U16(writer, operation.ClassId);
        WireWriter.Bytes(writer, operation.LogicalName.ToBytes());
        WireWriter.Byte(writer, (byte)operation.Index);
        if (operation.Kind == CosemOperationKind.Invoke)
        {
            if (operation.Selection != null)
            {
                throw new ArgumentException("ACTION does not have selective access.");
            }

            WireWriter.Byte(writer, operation.Value is null ? AxdrEncoding.False : AxdrEncoding.True);
            if (operation.Value != null)
            {
                axdr.Write(operation.Value, writer, 0, ref budget);
            }
        }
        else
        {
            WireWriter.Byte(writer, operation.Selection is null ? AxdrEncoding.False : AxdrEncoding.True);
            if (operation.Selection != null)
            {
                WireWriter.Byte(writer, operation.Selection.Selector);
                axdr.Write(operation.Selection.Parameters, writer, 0, ref budget);
            }

            if (operation.Kind == CosemOperationKind.Write && choice == ApduEncoding.NormalChoice)
            {
                axdr.Write(operation.Value ?? throw new ArgumentException("SET requires a value."), writer, 0, ref budget);
            }
        }
    }

    private static void WriteSetBlock(bool last, uint number, ImmutableArray<byte> data, IBufferWriter<byte> writer)
    {
        WireWriter.Byte(writer, last ? AxdrEncoding.True : AxdrEncoding.False);
        WireWriter.U32(writer, number);
        WireWriter.Length(writer, data.Length);
        WireWriter.Bytes(writer, data.AsSpan());
    }

    private static void WriteGetNextRequest(GetNextRequest next, IBufferWriter<byte> writer)
    {
        WireWriter.Byte(writer, (byte)ApduTag.GetRequest);
        WireWriter.Byte(writer, ApduEncoding.GetNextChoice);
        WireWriter.Byte(writer, next.InvokeIdAndPriority);
        WireWriter.U32(writer, next.BlockNumber);
    }

    private void WriteLnResponse(LnResponse response, IBufferWriter<byte> writer, ref int budget)
    {
        WireWriter.Byte(writer, response.Kind == CosemOperationKind.Read ? (byte)ApduTag.GetResponse : response.Kind == CosemOperationKind.Write ? (byte)ApduTag.SetResponse : (byte)ApduTag.ActionResponse);
        WireWriter.Byte(writer, ApduEncoding.NormalChoice);
        WireWriter.Byte(writer, response.InvokeIdAndPriority);
        if (response.Kind == CosemOperationKind.Read)
        {
            WriteResult(response.Result, writer, ref budget);
        }
        else
        {
            WireWriter.Byte(writer, (byte)response.Result.Error);
            if (response.Kind == CosemOperationKind.Invoke)
            {
                WireWriter.Byte(writer, response.Result.Value is null && response.ReturnDataError == null ? AxdrEncoding.False : AxdrEncoding.True);
                if (response.ReturnDataError.HasValue)
                {
                    WriteResult(new(response.ReturnDataError.Value), writer, ref budget);
                }
                else if (response.Result.Value != null)
                {
                    WriteResult(OperationResult.Ok(response.Result.Value), writer, ref budget);
                }
            }
        }
    }

    private static void WriteGetBlockResponse(GetBlockResponse block, IBufferWriter<byte> writer)
    {
        WireWriter.Byte(writer, (byte)ApduTag.GetResponse);
        WireWriter.Byte(writer, ApduEncoding.GetBlockChoice);
        WireWriter.Byte(writer, block.InvokeIdAndPriority);
        WireWriter.Byte(writer, block.LastBlock ? AxdrEncoding.True : AxdrEncoding.False);
        WireWriter.U32(writer, block.BlockNumber);
        WireWriter.Byte(writer, block.Error.HasValue ? ApduEncoding.ErrorResult : ApduEncoding.DataResult);
        if (block.Error.HasValue)
        {
            WireWriter.Byte(writer, (byte)block.Error.Value);
        }
        else
        {
            WireWriter.Length(writer, block.Data.Length);
            WireWriter.Bytes(writer, block.Data.AsSpan());
        }
    }

    private void WriteDataNotification(DataNotification notification, IBufferWriter<byte> writer, ref int budget)
    {
        WriteNotificationHeader(ApduTag.DataNotification, notification.LongInvokeIdAndPriority, notification.DateTime, writer);
        axdr.Write(notification.Body, writer, 0, ref budget);
    }

    private static void WriteNotificationHeader(ApduTag tag, uint invoke, ImmutableArray<byte> time, IBufferWriter<byte> writer)
    {
        if (time.Length is not (0 or DlmsValueSizes.DateTime))
        {
            throw new ArgumentException("Invalid notification date-time length.");
        }

        WireWriter.Byte(writer, (byte)tag);
        WireWriter.U32(writer, invoke);
        WireWriter.Length(writer, time.Length);
        WireWriter.Bytes(writer, time.AsSpan());
    }

    private ActionBlock ReadActionBlock(byte invoke, bool response, CosemOperation? method, ref WireReader reader)
    {
        bool last = Presence(ref reader);
        uint number = reader.U32();
        return new(invoke, response, last, number,
            reader.Take(reader.Length(limits.MaxMessageBytes)).ToArray().ToImmutableArray(), method);
    }

    private static void WriteActionBlock(ActionBlock block, IBufferWriter<byte> writer)
    {
        if (block.Method is { } method && (block.Response || method.Kind != CosemOperationKind.Invoke
            || method.Value is not null || method.Selection is not null))
        {
            throw new ArgumentException("Only the first request block can carry an ACTION method descriptor.");
        }

        WireWriter.Byte(writer, (byte)(block.Response ? ApduTag.ActionResponse : ApduTag.ActionRequest));
        WireWriter.Byte(writer, block.Response ? ApduEncoding.ActionResponseBlock
            : block.Method is null ? ApduEncoding.ActionRequestBlock : ApduEncoding.ActionFirstBlock);
        WireWriter.Byte(writer, block.InvokeIdAndPriority);
        if (block.Method is { } operation)
        {
            WireWriter.U16(writer, operation.ClassId);
            WireWriter.Bytes(writer, operation.LogicalName.ToBytes());
            WireWriter.Byte(writer, (byte)operation.Index);
        }

        WriteSetBlock(block.LastBlock, block.BlockNumber, block.Data, writer);
    }

    private GeneralBlockTransfer ReadGeneralBlock(ref WireReader reader)
    {
        byte control = reader.Byte();
        return new((control & 0x80) != 0, (control & 0x40) != 0, (byte)(control & 0x3f), reader.U16(),
            reader.U16(), reader.Take(reader.Length(limits.MaxMessageBytes)).ToArray().ToImmutableArray());
    }

    private static void WriteGeneralBlock(GeneralBlockTransfer block, IBufferWriter<byte> writer)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan(block.WindowSize, (byte)0x3f);
        WireWriter.Byte(writer, (byte)ApduTag.GeneralBlockTransfer);
        WireWriter.Byte(writer, (byte)((block.LastBlock ? 0x80 : 0) | (block.Streaming ? 0x40 : 0) | block.WindowSize));
        WireWriter.U16(writer, block.BlockNumber);
        WireWriter.U16(writer, block.BlockNumberAcknowledged);
        WireWriter.Length(writer, block.Data.Length);
        WireWriter.Bytes(writer, block.Data.AsSpan());
    }

    private static void WriteInitiateRequest(InitiateRequest initiate, IBufferWriter<byte> writer)
    {
        WireWriter.Byte(writer, (byte)ApduTag.InitiateRequest);
        WireWriter.Byte(writer, initiate.DedicatedKey.IsEmpty ? AxdrEncoding.False : AxdrEncoding.True);
        if (!initiate.DedicatedKey.IsEmpty)
        {
            WireWriter.Length(writer, initiate.DedicatedKey.Length);
            WireWriter.Bytes(writer, initiate.DedicatedKey.AsSpan());
        }

        WireWriter.Byte(writer, initiate.ResponseAllowed.HasValue ? AxdrEncoding.True : AxdrEncoding.False);
        if (initiate.ResponseAllowed.HasValue)
        {
            WireWriter.Byte(writer, initiate.ResponseAllowed.Value ? AxdrEncoding.True : AxdrEncoding.False);
        }

        WriteQos(initiate.QualityOfService, writer);
        WireWriter.Byte(writer, initiate.DlmsVersion);
        WriteConformance(initiate.Conformance, writer);
        WireWriter.U16(writer, initiate.MaxReceivePduSize);
    }

    private static void WriteInitiateResponse(InitiateResponse initiate, IBufferWriter<byte> writer)
    {
        WireWriter.Byte(writer, (byte)ApduTag.InitiateResponse);
        WriteQos(initiate.QualityOfService, writer);
        WireWriter.Byte(writer, initiate.DlmsVersion);
        WriteConformance(initiate.Conformance, writer);
        WireWriter.U16(writer, initiate.MaxReceivePduSize);
        WireWriter.U16(writer, initiate.VaaName);
    }
}
