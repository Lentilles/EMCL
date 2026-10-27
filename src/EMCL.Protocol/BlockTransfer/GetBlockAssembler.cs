using System.Collections.Immutable;
using EMCL.Core.Codecs;
using EMCL.Core.Operations;
using EMCL.Protocol.Apdu;

namespace EMCL.Protocol.BlockTransfer;

/// <summary>Assembles explicitly started GET transfers and rejects out-of-order, duplicate, and oversized blocks.</summary>
/// <remarks>Begin is explicit so a late block cannot silently create a new transfer. Accept aborts a transfer after an invalid block. Instances are thread-safe.</remarks>
public sealed class GetBlockAssembler
{
    private sealed class Transfer
    {
        public uint Expected = 1;
        public int Count;
        public List<byte> Bytes = [];
    }

    private readonly Dictionary<GetExchangeKey, Transfer> transfers = [];
    private readonly ProtocolLimits limits;
    /// <summary>Creates an independent GET block assembler with explicit transfer resource limits.</summary>
    /// <param name="limits">Optional resource bounds; null selects validated defaults.</param>
    public GetBlockAssembler(ProtocolLimits? limits = null)
    {
        this.limits = ProtocolLimits.Resolve(limits);
    }

    /// <summary>Starts a transfer expecting block number one and rejects reuse of an already active key.</summary>
    /// <param name="key">The exact class key or independently scoped GET transfer key.</param>
    /// <exception cref="InvalidOperationException">A transfer with this key is already active.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The invoke identifier exceeds four bits.</exception>
    public void Begin(GetExchangeKey key)
    {
        if (key.InvokeId > InvokeIdEncoding.IdMask)
        {
            throw new ArgumentOutOfRangeException(nameof(key));
        }

        lock (transfers)
        {
            if (!transfers.TryAdd(key, new()))
            {
                throw new InvalidOperationException("GET transfer is already active.");
            }
        }
    }

    /// <summary>Discards any pending bytes for the specified transfer.</summary>
    /// <param name="key">The exact class key or independently scoped GET transfer key.</param>
    public void Abort(GetExchangeKey key)
    {
        lock (transfers)
        {
            transfers.Remove(key);
        }
    }

    /// <summary>Accepts the next block, completes the transfer, or aborts it with an explicit result code.</summary>
    /// <param name="key">The exact class key or independently scoped GET transfer key.</param>
    /// <param name="block">The next GET response block.</param>
    /// <returns>The assembly status, complete bytes only on success, or a transfer error.</returns>
    public BlockAssemblyResult Accept(GetExchangeKey key, GetBlockResponse block)
    {
        lock (transfers)
        {
            if (!transfers.TryGetValue(key, out var transfer))
            {
                return new(false, [], CosemError.NoLongGetInProgress);
            }

            CosemError error = CosemError.Success;
            if ((block.InvokeIdAndPriority & InvokeIdEncoding.IdMask) != key.InvokeId || block.BlockNumber != transfer.Expected)
            {
                error = CosemError.DataBlockNumberInvalid;
            }
            else if (block.Error.HasValue)
            {
                error = block.Error.Value == CosemError.Success ? CosemError.OtherReason : block.Error.Value;
            }
            else if (++transfer.Count > limits.MaxBlockCount || block.Data.Length > limits.MaxMessageBytes - transfer.Bytes.Count)
            {
                error = CosemError.LongGetAborted;
            }

            if (error != CosemError.Success)
            {
                transfers.Remove(key);
                return new(false, [], error);
            }

            transfer.Bytes.AddRange(block.Data);
            if (block.LastBlock)
            {
                transfers.Remove(key);
                return new(true, transfer.Bytes.ToImmutableArray());
            }

            if (transfer.Expected == uint.MaxValue)
            {
                transfers.Remove(key);
                return new(false, [], CosemError.LongGetAborted);
            }

            transfer.Expected++;
            return new(false, []);
        }
    }
}
