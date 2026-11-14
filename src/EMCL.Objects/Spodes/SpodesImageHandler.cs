using EMCL.Core.Codecs;
using EMCL.Core.Objects;
using EMCL.Core.Operations;
using EMCL.Core.Values;
using EMCL.Objects.Handlers;

namespace EMCL.Objects.Spodes;

internal sealed class SpodesImageHandler(ISpodesImageTarget? target, ProtocolLimits limits) : StoredValueHandler
{
    private const sbyte BlockSize = 2;
    private const sbyte TransferredBlocks = 3;
    private const sbyte FirstMissingBlock = 4;
    private const sbyte Enabled = 5;
    private const sbyte Status = 6;
    private const sbyte ActivationInfo = 7;
    private const sbyte Initiate = 1;
    private const sbyte TransferBlock = 2;
    private const sbyte Verify = 3;
    private const sbyte Activate = 4;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<CosemObject, Transfer> transfers = new();

    public override ValueTask<OperationResult> ReadAsync(CosemObject obj, CosemOperation operation,
        OperationContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!obj.State.TryGet(operation.Index, out _))
        {
            var value = operation.Index switch
            {
                TransferredBlocks => new DlmsValue(DlmsType.BitString, new DlmsBitString(0, [])),
                FirstMissingBlock => DlmsValue.Number(DlmsType.UInt32, 0),
                Enabled => new DlmsValue(DlmsType.Boolean, true),
                Status => DlmsValue.Number(DlmsType.Enum, (int)ImageStatus.NotInitiated),
                ActivationInfo => DlmsValue.Array(),
                _ => null
            };
            if (value is not null)
            {
                return ValueTask.FromResult(OperationResult.Ok(value));
            }
        }

        return base.ReadAsync(obj, operation, context, cancellationToken);
    }

    public override ValueTask<OperationResult> WriteAsync(CosemObject obj, CosemOperation operation,
        OperationContext context, CancellationToken cancellationToken)
    {
        if (operation.Index is not (BlockSize or Enabled))
        {
            return ValueTask.FromResult(new OperationResult(CosemResultCode.ReadWriteDenied));
        }

        if (operation.Index == BlockSize && transfers.ContainsKey(obj))
        {
            return ValueTask.FromResult(new OperationResult(CosemResultCode.TemporaryFailure));
        }

        return base.WriteAsync(obj, operation, context, cancellationToken);
    }

    public override async ValueTask<OperationResult> InvokeAsync(CosemObject obj, CosemOperation operation,
        OperationContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (obj.State.TryGet(Enabled, out var enabled) && enabled.Value is false)
        {
            return new(CosemResultCode.TemporaryFailure);
        }

        if (operation.Index == Initiate)
        {
            return Start(obj, operation.Value!);
        }

        if (!transfers.TryGetValue(obj, out var transfer))
        {
            return new(CosemResultCode.TemporaryFailure);
        }

        if (operation.Index == TransferBlock)
        {
            return StoreBlock(obj, transfer, operation.Value!);
        }

        if (target is null)
        {
            return new(CosemResultCode.ObjectUnavailable);
        }

        if (operation.Index == Verify && transfer.FirstMissing == transfer.Count && !transfer.Verified)
        {
            SetStatus(obj, ImageStatus.VerificationInitiated);
            try
            {
                var signature = await target.VerifyAsync(obj.LogicalName, transfer.Identifier, transfer.Image,
                    cancellationToken).ConfigureAwait(false);
                obj.State.Set(ActivationInfo, DlmsValue.Array(DlmsValue.Structure(
                    DlmsValue.Number(DlmsType.UInt32, transfer.Image.Length), DlmsValue.Octets(transfer.Identifier),
                    DlmsValue.Octets(signature.Span))));
                transfer.Verified = true;
                SetStatus(obj, ImageStatus.VerificationSuccessful);
                return OperationResult.Ok();
            }
            catch (OperationCanceledException)
            {
                SetStatus(obj, ImageStatus.VerificationFailed);
                throw;
            }
            catch (Exception error) when (error is IOException or System.Security.Cryptography.CryptographicException or InvalidOperationException)
            {
                SetStatus(obj, ImageStatus.VerificationFailed);
                return new(CosemResultCode.OtherReason);
            }
        }

        if (operation.Index == Activate && transfer.Verified)
        {
            SetStatus(obj, ImageStatus.ActivationInitiated);
            try
            {
                await target.ActivateAsync(obj.LogicalName, transfer.Identifier, transfer.Image, cancellationToken).ConfigureAwait(false);
                transfers.TryRemove(obj, out _);
                SetStatus(obj, ImageStatus.ActivationSuccessful);
                return OperationResult.Ok();
            }
            catch (OperationCanceledException)
            {
                SetStatus(obj, ImageStatus.ActivationFailed);
                throw;
            }
            catch (Exception error) when (error is IOException or InvalidOperationException)
            {
                SetStatus(obj, ImageStatus.ActivationFailed);
                return new(CosemResultCode.HardwareFault);
            }
        }

        return operation.Index == Verify && transfer.Verified ? OperationResult.Ok() : new(CosemResultCode.TemporaryFailure);
    }

    private OperationResult Start(CosemObject obj, DlmsValue value)
    {
        if (!obj.State.TryGet(BlockSize, out var storedSize))
        {
            return new(CosemResultCode.ObjectUnavailable);
        }

        ulong blockSize = storedSize.AsUInt64();
        ulong size = value.Items[1].AsUInt64();
        if (blockSize == 0 || size == 0 || size > (ulong)limits.MaxMessageBytes)
        {
            return new(CosemResultCode.ScopeOfAccessViolated);
        }

        ulong count = 1 + (size - 1) / blockSize;
        if (count > (ulong)limits.MaxBlockCount)
        {
            return new(CosemResultCode.ScopeOfAccessViolated);
        }

        var identifier = value.Items[0].Bytes;
        if (transfers.TryGetValue(obj, out var existing) && !existing.Verified
            && existing.Identifier.AsSpan().SequenceEqual(identifier.AsSpan()) && (ulong)existing.Image.Length == size)
        {
            return OperationResult.Ok();
        }

        var transfer = new Transfer(identifier.ToArray(), new byte[(int)size], (int)Math.Min(size, blockSize), (int)count);
        transfers[obj] = transfer;
        obj.State.Set(ActivationInfo, DlmsValue.Array());
        SetProgress(obj, transfer);
        SetStatus(obj, ImageStatus.TransferInitiated);
        return OperationResult.Ok();
    }

    private static OperationResult StoreBlock(CosemObject obj, Transfer transfer, DlmsValue parameter)
    {
        ulong number = parameter.Items[0].AsUInt64();
        if (number >= (ulong)transfer.Count)
        {
            return new(CosemResultCode.DataBlockNumberInvalid);
        }

        int index = (int)number;
        int offset = index * transfer.BlockSize;
        var data = parameter.Items[1].Bytes.AsSpan();
        int expected = Math.Min(transfer.BlockSize, transfer.Image.Length - offset);
        if (data.Length != expected)
        {
            return new(CosemResultCode.TypeUnmatched);
        }

        byte mask = (byte)(0x80 >> (index % 8));
        ref byte bits = ref transfer.Bitmap[index / 8];
        var destination = transfer.Image.AsSpan(offset, expected);
        if ((bits & mask) != 0)
        {
            return data.SequenceEqual(destination) ? OperationResult.Ok() : new(CosemResultCode.DataBlockUnavailable);
        }

        data.CopyTo(destination);
        bits |= mask;
        while (transfer.FirstMissing < transfer.Count &&
            (transfer.Bitmap[transfer.FirstMissing / 8] & (0x80 >> (transfer.FirstMissing % 8))) != 0)
        {
            transfer.FirstMissing++;
        }

        SetProgress(obj, transfer);
        return OperationResult.Ok();
    }

    private static void SetProgress(CosemObject obj, Transfer transfer)
    {
        obj.State.Set(TransferredBlocks, new(DlmsType.BitString, new DlmsBitString(transfer.Count, transfer.Bitmap)));
        obj.State.Set(FirstMissingBlock, DlmsValue.Number(DlmsType.UInt32, transfer.FirstMissing));
    }

    private static void SetStatus(CosemObject obj, ImageStatus status)
        => obj.State.Set(Status, DlmsValue.Number(DlmsType.Enum, (int)status));

    private sealed class Transfer(byte[] identifier, byte[] image, int blockSize, int count)
    {
        internal byte[] Identifier { get; } = identifier;
        internal byte[] Image { get; } = image;
        internal int BlockSize { get; } = blockSize;
        internal int Count { get; } = count;
        internal byte[] Bitmap { get; } = new byte[(int)((count + 7L) / 8)];
        internal int FirstMissing
        {
            get; set;
        }
        internal bool Verified
        {
            get; set;
        }
    }

    private enum ImageStatus
    {
        NotInitiated, TransferInitiated, VerificationInitiated, VerificationSuccessful,
        VerificationFailed, ActivationInitiated, ActivationSuccessful, ActivationFailed
    }
}
