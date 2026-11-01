using System.Buffers;
using System.Runtime.ExceptionServices;
using EMCL.Client.Exceptions;
using EMCL.Client.Internal;
using EMCL.Core.Codecs;
using EMCL.Core.Objects;
using EMCL.Core.Operations;
using EMCL.Core.Values;
using EMCL.Objects.Association;
using EMCL.Objects.Definitions;
using EMCL.Protocol.Apdu;
using EMCL.Protocol.Axdr;
using EMCL.Protocol.BlockTransfer;

namespace EMCL.Client.Sessions;

public sealed partial class DlmsClientSession
{
    private const sbyte ReservedMemberIndex = 0;
    private static readonly LogicalName CurrentAssociation = LogicalName.Parse("0.0.40.0.0.255");

    /// <inheritdoc />
    public ValueTask<LnResponse> ExecuteAsync(CosemOperation operation, CancellationToken cancellationToken = default)
        => ExecuteObservedAsync(operation, null, cancellationToken);

    private async ValueTask<LnResponse> ExecuteObservedAsync(CosemOperation operation,
        Func<ReadOnlyMemory<byte>, CancellationToken, ValueTask>? onGetBlock, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            RequireState(ClientSessionState.Associated);
            ValidateOperation(operation);
            if (pendingCalls++ == 0)
            {
                callsIdle = new(TaskCreationOptions.RunContinuationsAsynchronously);
            }
        }

        try
        {
            return await ExecuteCoreAsync(operation, onGetBlock, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            lock (gate)
            {
                if (--pendingCalls == 0)
                {
                    callsIdle.TrySetResult();
                }
            }
        }
    }

    private async ValueTask<LnResponse> ExecuteCoreAsync(CosemOperation operation,
        Func<ReadOnlyMemory<byte>, CancellationToken, ValueTask>? onGetBlock, CancellationToken cancellationToken)
    {
        using var queued = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lifetimeToken);
        try
        {
            await slots.WaitAsync(queued.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            ExceptionDispatchInfo.Capture(SessionFailure()).Throw();
            throw;
        }

        PendingOperation? pending = null;
        bool attempted = false;
        try
        {
            lock (gate)
            {
                RequireState(ClientSessionState.Associated);
                pending = RegisterOperation(operation.Kind);
            }

            using var deadline = new ExchangeDeadline(options, cancellationToken, lifetimeToken);
            try
            {
                await SendAsync(new LnRequest(pending.Invoke, operation), SendLimit(), deadline.Token,
                    () => attempted = true).ConfigureAwait(false);
                return await ReceiveOperationAsync(pending, onGetBlock, deadline.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                var error = deadline.TranslateCancellation(SessionFailure());
                if (attempted)
                {
                    Fault(error);
                }

                ExceptionDispatchInfo.Capture(error).Throw();
                throw;
            }
            catch (Exception error)
            {
                if (attempted)
                {
                    Fault(error);
                }

                throw;
            }
        }
        finally
        {
            if (pending != null)
            {
                blocks.Abort(ExchangeKey(pending));
                lock (gate)
                {
                    operations.Remove(pending.Id);
                    if (--activeOperations == 0)
                    {
                        idle.TrySetResult();
                    }
                }
            }

            slots.Release();
        }
    }

    /// <inheritdoc />
    public async ValueTask<DlmsValue> GetAsync(ushort classId, LogicalName logicalName, sbyte attribute,
        SelectiveAccess? selection = null, CancellationToken cancellationToken = default)
    {
        var response = await ExecuteAsync(new(CosemOperationKind.Read, classId, logicalName, attribute,
            Selection: selection), cancellationToken).ConfigureAwait(false);
        ThrowRemoteError(response);
        return response.Result.Value!;
    }

    /// <inheritdoc />
    public async ValueTask SetAsync(ushort classId, LogicalName logicalName, sbyte attribute, DlmsValue value,
        SelectiveAccess? selection = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(value);
        var response = await ExecuteAsync(new(CosemOperationKind.Write, classId, logicalName, attribute, value, selection),
            cancellationToken).ConfigureAwait(false);
        ThrowRemoteError(response);
    }

    /// <inheritdoc />
    public async ValueTask<DlmsValue?> ActionAsync(ushort classId, LogicalName logicalName, sbyte method,
        DlmsValue? parameter = null, CancellationToken cancellationToken = default)
    {
        var response = await ExecuteAsync(new(CosemOperationKind.Invoke, classId, logicalName, method, parameter),
            cancellationToken).ConfigureAwait(false);
        ThrowRemoteError(response);
        return response.Result.Value;
    }

    /// <inheritdoc />
    public async ValueTask<ImportedAssociation> LoadCatalogAsync(AssociationImporter importer, string logicalDeviceId,
        string associationId, byte associationVersion, LogicalName? associationObject = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(importer);
        ArgumentException.ThrowIfNullOrWhiteSpace(logicalDeviceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(associationId);
        if (associationVersion is not (AssociationLnMembers.Version1 or AssociationLnMembers.Version3))
        {
            throw new NotSupportedException("An explicit Association LN version 1 or 3 is required.");
        }

        await using var stream = await OpenObjectListAsync(importer, associationVersion, associationObject,
            cancellationToken).ConfigureAwait(false);
        var entries = new List<DlmsValue>(stream.TotalCount);
        await foreach (var entry in stream.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            entries.Add(entry.RawValue);
        }

        cancellationToken.ThrowIfCancellationRequested();
        return importer.Import(DlmsValue.Array([.. entries]), logicalDeviceId, associationId, associationVersion);
    }

    private PendingOperation RegisterOperation(CosemOperationKind kind)
    {
        while (operations.ContainsKey(nextInvokeId))
        {
            nextInvokeId = (byte)((nextInvokeId + 1) & InvokeIdEncoding.IdMask);
        }

        var pending = new PendingOperation(InvokeIdEncoding.Create(nextInvokeId, options.HighPriority), kind);
        operations.Add(pending.Id, pending);
        nextInvokeId = (byte)((nextInvokeId + 1) & InvokeIdEncoding.IdMask);
        if (activeOperations++ == 0)
        {
            idle = new(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        return pending;
    }

    private async ValueTask<LnResponse> ReceiveOperationAsync(PendingOperation pending,
        Func<ReadOnlyMemory<byte>, CancellationToken, ValueTask>? onGetBlock, CancellationToken cancellationToken)
    {
        var response = await pending.ReceiveAsync(cancellationToken).ConfigureAwait(false);
        if (response is LnResponse normal)
        {
            if (normal.Kind == CosemOperationKind.Read && normal.Result.Error == CosemError.Success && normal.Result.Value == null)
            {
                throw new DlmsProtocolException("A successful GET response requires a Data value.");
            }

            return normal;
        }

        if ((negotiated!.Conformance & DlmsConformance.BlockTransferWithGet) == DlmsConformance.None)
        {
            throw new DlmsProtocolException("The peer used GET block transfer without negotiation.");
        }

        var key = ExchangeKey(pending);
        blocks.Begin(key);
        while (true)
        {
            var block = (GetBlockResponse)response;
            var assembled = blocks.Accept(key, block);
            if (assembled.Error != CosemError.Success)
            {
                if (block.Error.HasValue && block.Error.Value == assembled.Error)
                {
                    return new(CosemOperationKind.Read, pending.Invoke, new(assembled.Error));
                }

                throw new DlmsProtocolException($"The GET block sequence or resource bound is invalid: {assembled.Error}.");
            }

            if (onGetBlock != null)
            {
                await onGetBlock(block.Data.ToArray(), cancellationToken).ConfigureAwait(false);
            }

            if (assembled.Complete)
            {
                var value = new AxdrCodec(options.Limits).Decode(new ReadOnlySequence<byte>(assembled.Data.ToArray()));
                if (value.Status != DecodeStatus.Done || value.Consumed != assembled.Data.Length)
                {
                    throw new DlmsProtocolException("The completed GET blocks do not contain exactly one A-XDR Data value.");
                }

                return new(CosemOperationKind.Read, pending.Invoke, OperationResult.Ok(value.Value));
            }

            lock (gate)
            {
                pending.PrepareNextBlock();
            }

            // GET-next acknowledges the last received block, rather than naming the next block number.
            await SendAsync(new GetNextRequest(pending.Invoke, block.BlockNumber), SendLimit(), cancellationToken).ConfigureAwait(false);
            response = await pending.ReceiveAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private GetExchangeKey ExchangeKey(PendingOperation pending) => new(exchangeScope, exchangeScope, exchangeScope, pending.Id);

    private void ValidateOperation(CosemOperation operation)
    {
        var required = operation.Kind switch
        {
            CosemOperationKind.Read => DlmsConformance.Get,
            CosemOperationKind.Write => DlmsConformance.Set,
            CosemOperationKind.Invoke => DlmsConformance.Action,
            _ => throw new ArgumentOutOfRangeException(nameof(operation))
        };
        if ((negotiated!.Conformance & required) == DlmsConformance.None)
        {
            throw new NotSupportedException("The requested service was not negotiated.");
        }

        if (operation.Index == ReservedMemberIndex || operation.Kind == CosemOperationKind.Write && operation.Value == null
            || operation.Kind == CosemOperationKind.Invoke && operation.Selection != null)
        {
            throw new ArgumentException("The operation has an invalid member index, missing SET value, or ACTION selection.", nameof(operation));
        }

        if (operation.Selection != null && (negotiated.Conformance & DlmsConformance.SelectiveAccess) == DlmsConformance.None)
        {
            throw new NotSupportedException("Selective access was not negotiated.");
        }
    }

    private static void ThrowRemoteError(LnResponse response)
    {
        if (response.Result.Error != CosemError.Success || response.ReturnDataError is not (null or CosemError.Success))
        {
            throw new DlmsOperationException(response);
        }
    }
}
