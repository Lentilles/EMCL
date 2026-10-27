using System.Buffers;
using EMCL.Core.Codecs;
using EMCL.Core.Operations;
using EMCL.Core.Pipeline;
using EMCL.Protocol.Apdu;
using EMCL.Transport.Channels;

namespace EMCL.Messaging;

/// <summary>Combines a complete-APDU channel and APDU codec without managing association state or request correlation.</summary>
/// <remarks>Session state, request/response correlation, authentication, block-transfer lifecycle, and transport connection establishment belong to the application.</remarks>
/// <param name="channel">The complete-APDU channel owned by the endpoint.</param>
/// <param name="codec">An optional APDU codec; null selects the standard supported APDU codec.</param>
public sealed class DlmsEndpoint(IApduChannel channel, IIncrementalCodec<DlmsApdu>? codec = null) : IAsyncDisposable
{
    private readonly IApduChannel channel = channel ?? throw new ArgumentNullException(nameof(channel));
    private readonly IIncrementalCodec<DlmsApdu> codec = codec ?? new ApduCodec();
    private int disposed;
    /// <summary>Receives and decodes exactly one complete APDU, rejecting trailing or truncated data.</summary>
    /// <param name="cancellationToken">A token that requests cancellation of the asynchronous operation.</param>
    /// <returns>The decoded APDU, or null at clean end of input.</returns>
    /// <exception cref="System.IO.InvalidDataException">The payload cannot be decoded as exactly one supported APDU.</exception>
    /// <exception cref="OperationCanceledException">Cancellation was requested.</exception>
    public async ValueTask<DlmsApdu?> ReceiveAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        var payload = await channel.ReceiveAsync(cancellationToken).ConfigureAwait(false);
        if (!payload.HasValue)
        {
            return null;
        }

        var result = codec.Decode(new ReadOnlySequence<byte>(payload.Value));
        if (result.Status != DecodeStatus.Done || result.Consumed != payload.Value.Length)
        {
            throw new InvalidDataException($"APDU decode {result.Status}: {result.Error}; one complete APDU is required.");
        }

        return result.Value;
    }

    /// <summary>Encodes and sends one complete APDU through the configured channel.</summary>
    /// <param name="apdu">One complete application PDU.</param>
    /// <param name="cancellationToken">A token that requests cancellation of the asynchronous operation.</param>
    /// <returns>An awaitable representing the complete send.</returns>
    public ValueTask SendAsync(DlmsApdu apdu, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        return channel.SendAsync(codec.EncodeBytes(apdu), cancellationToken);
    }

    /// <summary>Executes a normal LN request through a supplied operation pipeline and creates the corresponding normal response.</summary>
    /// <param name="request">The decoded normal LN request.</param>
    /// <param name="pipeline">The operation pipeline.</param>
    /// <param name="context">The logical-device, association, and protection context.</param>
    /// <param name="cancellationToken">A token that requests cancellation of the asynchronous operation.</param>
    /// <returns>A normal LN response preserving the request invoke-id-and-priority octet.</returns>
    public static async ValueTask<LnResponse> ExecuteAsync(LnRequest request, IDlmsPipeline pipeline, OperationContext context, CancellationToken cancellationToken = default)
    {
        var result = await pipeline.ExecuteAsync(request.Operation, context, cancellationToken).ConfigureAwait(false);
        return new(request.Operation.Kind, request.InvokeIdAndPriority, result);
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync() => Interlocked.Exchange(ref disposed, 1) != 0 ? ValueTask.CompletedTask : channel.DisposeAsync();
}
