using System.Buffers;
using EMCL.Core.Codecs;
using EMCL.Transport.Streams;

namespace EMCL.Transport.Channels;

/// <summary>Combines a byte transport, incremental frame codec, and payload adapter into a complete-APDU channel.</summary>
/// <remarks>One reader and one writer can run concurrently. Framing failures invalidate the channel. Dispose only after active operations have completed.</remarks>
/// <typeparam name="TFrame">The transport frame representation.</typeparam>
public sealed class FramedApduChannel<TFrame> : IApduChannel
{
    private readonly IByteTransport transport;
    private readonly IIncrementalCodec<TFrame> codec;
    private readonly IFramePayloadAdapter<TFrame> adapter;
    private readonly ProtocolLimits limits;
    private readonly SemaphoreSlim readLock = new(1, 1), writeLock = new(1, 1);
    private byte[] buffer = new byte[4096];
    private int count;
    private volatile bool failed;
    private int disposed;
    /// <summary>Creates a complete-APDU channel with independently replaceable transport, frame codec, and payload adapter.</summary>
    /// <param name="transport">The byte transport owned by the channel.</param>
    /// <param name="codec">The codec used to encode or decode complete values.</param>
    /// <param name="adapter">The frame-to-APDU payload adapter.</param>
    /// <param name="limits">Optional resource bounds; null selects validated defaults.</param>
    public FramedApduChannel(IByteTransport transport, IIncrementalCodec<TFrame> codec, IFramePayloadAdapter<TFrame> adapter, ProtocolLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(transport);
        ArgumentNullException.ThrowIfNull(codec);
        ArgumentNullException.ThrowIfNull(adapter);
        this.transport = transport;
        this.codec = codec;
        this.adapter = adapter;
        this.limits = ProtocolLimits.Resolve(limits);
    }

    /// <inheritdoc />
    public async ValueTask<ReadOnlyMemory<byte>?> ReceiveAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        await readLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (failed)
            {
                throw new InvalidOperationException("Channel framing failed; create a new channel or perform explicit recovery.");
            }

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var result = codec.Decode(new ReadOnlySequence<byte>(buffer.AsMemory(0, count)));
                if (result.Status == DecodeStatus.Done)
                {
                    if (result.Consumed <= 0 || result.Consumed > count)
                    {
                        throw new InvalidDataException("Frame codec returned an invalid consumed count.");
                    }

                    int used = checked((int)result.Consumed);
                    buffer.AsSpan(used, count - used).CopyTo(buffer);
                    count -= used;
                    var payload = adapter.Accept(result.Value!);
                    if (payload.HasValue)
                    {
                        if (payload.Value.Length > limits.MaxMessageBytes)
                        {
                            throw new InvalidDataException("APDU limit exceeded.");
                        }

                        return payload.Value;
                    }

                    continue;
                }

                if (result.Status != DecodeStatus.NeedMoreData)
                {
                    throw new InvalidDataException($"Frame decode {result.Status}: {result.Error}");
                }

                bool idle = adapter.IsIdle(buffer.AsSpan(0, count));
                if (idle && count > adapter.IdleBytesToRetain)
                {
                    int retain = adapter.IdleBytesToRetain;
                    buffer.AsSpan(count - retain, retain).CopyTo(buffer);
                    count = retain;
                }

                // A bounded overhead permits frame headers even with a small APDU limit.
                int maximum = checked(limits.MaxMessageBytes + 64);
                if (count >= maximum)
                {
                    throw new InvalidDataException("Frame buffer limit exceeded.");
                }

                if (count == buffer.Length)
                {
                    Array.Resize(ref buffer, Math.Min(maximum, checked(buffer.Length * 2)));
                }

                int received = await transport.ReadAsync(buffer.AsMemory(count, Math.Min(buffer.Length - count, maximum - count)), cancellationToken).ConfigureAwait(false);
                if (received == 0)
                {
                    if ((count == 0 || adapter.IsIdle(buffer.AsSpan(0, count))) && !adapter.HasPending)
                    {
                        return null;
                    }

                    throw new EndOfStreamException("Truncated frame or segmented message.");
                }

                count += received;
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            failed = true;
            throw;
        }
        finally
        {
            readLock.Release();
        }
    }

    /// <inheritdoc />
    public async ValueTask SendAsync(ReadOnlyMemory<byte> apdu, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        if (apdu.Length > limits.MaxMessageBytes)
        {
            throw new ArgumentException("APDU limit exceeded.");
        }

        await writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (failed)
            {
                throw new InvalidOperationException("Channel has failed.");
            }

            foreach (var frame in adapter.Frame(apdu))
            {
                cancellationToken.ThrowIfCancellationRequested();
                await transport.WriteAsync(codec.EncodeBytes(frame), cancellationToken).ConfigureAwait(false);
            }
        }
        catch
        {
            failed = true;
            throw;
        }
        finally
        {
            writeLock.Release();
        }
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return ValueTask.CompletedTask;
        }

        readLock.Dispose();
        writeLock.Dispose();
        return transport.DisposeAsync();
    }
}
