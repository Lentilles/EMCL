using System.Buffers;
using EMCL.Core.Codecs;
using EMCL.Framing;
using EMCL.Framing.Hdlc.Frames;
using EMCL.Framing.Hdlc.Llc;
using EMCL.Protocol.Acse;
using EMCL.Protocol.Apdu;
using EMCL.Transport.Channels;
using EMCL.Transport.Streams;

namespace EMCL.Examples.AssociationDump.Transport;

internal sealed class HdlcTcpApduChannel : IApduChannel
{
    private const int MaximumStaleFramesDuringLinkSetup = 32;
    private const int DiagnosticPrefixLength = 16;
    private static readonly TimeSpan DisconnectTimeout = TimeSpan.FromSeconds(2);
    private readonly IByteTransport transport;
    private readonly HdlcCodec codec = new();
    private readonly HdlcAddress local;
    private readonly HdlcAddress remote;
    private readonly TextWriter output;
    private readonly bool redactAarq;
    private readonly object traceLock = new();
    private readonly SemaphoreSlim writeLock = new(1, 1);
    private readonly byte[] input = new byte[HdlcFrameFormat.MaximumFrameLength + HdlcFrameFormat.BoundaryFlagsLength];
    private HdlcApduAdapter? adapter;
    private int inputCount;
    private int messageCount;
    private int disposed;
    private byte nextReceiveSequence;
    private byte nextSendSequence;

    private HdlcTcpApduChannel(IByteTransport transport, HdlcAddress local, HdlcAddress remote,
        TextWriter output, bool redactAarq)
    {
        this.transport = transport;
        this.local = local;
        this.remote = remote;
        this.output = output;
        this.redactAarq = redactAarq;
    }

    internal static async Task<HdlcTcpApduChannel> ConnectAsync(IByteTransport transport, HdlcAddress local,
        HdlcAddress remote, ProtocolLimits limits, TimeSpan timeout, TextWriter output, bool redactAarq,
        CancellationToken cancellationToken)
    {
        var channel = new HdlcTcpApduChannel(transport, local, remote, output, redactAarq);
        try
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(timeout);
            try
            {
                await channel.WriteFrameAsync(new(remote, local, HdlcControl.Snrm, []), deadline.Token);
                var response = await channel.WaitForUaAsync(deadline.Token);

                int maximumInformation = HdlcNegotiation.MaximumOutgoingInformation(response.Information);
                channel.adapter = new(local, remote, maxInformationBytes: maximumInformation, limits: limits);
                return channel;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException("HDLC link setup timed out while waiting for UA.");
            }
        }
        catch
        {
            await channel.DisposeAsync();
            throw;
        }
    }

    public async ValueTask<ReadOnlyMemory<byte>?> ReceiveAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        var dataAdapter = adapter ?? throw new InvalidOperationException("The HDLC link is not established.");
        while (true)
        {
            var frame = await ReadFrameAsync(cancellationToken);
            ValidateAddresses(frame);
            if (frame.IsInformation)
            {
                if (frame.SendSequence != nextReceiveSequence || frame.ReceiveSequence != nextSendSequence)
                {
                    throw new InvalidDataException("Unexpected HDLC I-frame sequence or acknowledgement.");
                }

                nextReceiveSequence = (byte)((nextReceiveSequence + 1) & HdlcFrameFormat.SequenceMask);
                var apdu = dataAdapter.Accept(frame);
                if (frame.Segmented)
                {
                    await WriteFrameAsync(new(remote, local,
                        HdlcControl.CreateReceiveReady(nextReceiveSequence), []), cancellationToken);
                }

                if (apdu.HasValue)
                {
                    return apdu;
                }

                continue;
            }

            if (frame.IsUnnumberedInformation)
            {
                return dataAdapter.Accept(frame);
            }

            switch (frame.Control & HdlcControl.SupervisoryTypeMask)
            {
                case HdlcControl.ReceiveReady:
                    if (frame.ReceiveSequence != nextSendSequence)
                    {
                        throw new InvalidDataException("Unexpected HDLC RR acknowledgement.");
                    }

                    continue;
                case HdlcControl.ReceiveNotReady:
                    throw new IOException("The meter reported HDLC receive-not-ready (RNR).");
                case HdlcControl.Reject:
                    throw new IOException("The meter rejected an HDLC information frame (REJ).");
                default:
                    throw new InvalidDataException($"Unexpected HDLC control {frame.Control:X2}.");
            }
        }
    }

    public async ValueTask SendAsync(ReadOnlyMemory<byte> apdu, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        var dataAdapter = adapter ?? throw new InvalidOperationException("The HDLC link is not established.");
        await writeLock.WaitAsync(cancellationToken);
        try
        {
            dataAdapter.ReceiveSequence = nextReceiveSequence;
            var frames = dataAdapter.Frame(apdu).ToArray();
            if (frames.Length != 1)
            {
                throw new NotSupportedException("The example HDLC channel requires an outgoing APDU to fit one negotiated information frame.");
            }

            nextSendSequence = dataAdapter.NextSendSequence;
            await WriteFrameCoreAsync(frames[0], cancellationToken);
        }
        finally
        {
            writeLock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        if (adapter != null)
        {
            using var deadline = new CancellationTokenSource(DisconnectTimeout);
            try
            {
                await WriteFrameAsync(new(remote, local, HdlcControl.Disc, []), deadline.Token);
            }
            catch (Exception error) when (error is IOException or OperationCanceledException or ObjectDisposedException)
            {
                // The TCP gateway may have closed first; local disposal still succeeds.
            }
        }

        writeLock.Dispose();
        await transport.DisposeAsync();
    }

    private async Task<HdlcFrame> WaitForUaAsync(CancellationToken cancellationToken)
    {
        for (int staleFrames = 0; staleFrames <= MaximumStaleFramesDuringLinkSetup; staleFrames++)
        {
            var frame = await ReadFrameAsync(cancellationToken);
            if (frame.Destination != local || frame.Source != remote)
            {
                // Some TCP gateways flush frames from a previous client when a new socket opens.
                continue;
            }

            ValidateAddresses(frame);
            if (frame.Control == HdlcControl.Ua)
            {
                return frame;
            }

            if (frame.Control == HdlcControl.Dm)
            {
                throw new IOException("The meter rejected HDLC link establishment (DM).");
            }
        }

        throw new InvalidDataException("The meter did not send HDLC UA after SNRM; stale frames exceeded the limit.");
    }

    private async Task<HdlcFrame> ReadFrameAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            var result = codec.Decode(new ReadOnlySequence<byte>(input.AsMemory(0, inputCount)));
            if (result.Status == DecodeStatus.Done)
            {
                var frame = result.Value!;
                int consumed = checked((int)result.Consumed);
                Trace("RX", frame, input.AsSpan(0, consumed + 1));
                input.AsSpan(consumed, inputCount - consumed).CopyTo(input);
                inputCount -= consumed;
                return frame;
            }

            if (result.Status != DecodeStatus.NeedMoreData)
            {
                string prefix = Convert.ToHexString(input.AsSpan(0, Math.Min(inputCount, DiagnosticPrefixLength)));
                throw new InvalidDataException($"HDLC frame decode {result.Status}: {result.Error}; prefix={prefix}.");
            }

            if (inputCount > 1 && input.AsSpan(0, inputCount).IndexOfAnyExcept(HdlcFrameFormat.Flag) < 0)
            {
                input[0] = HdlcFrameFormat.Flag;
                inputCount = 1;
            }

            if (inputCount == input.Length)
            {
                throw new InvalidDataException("HDLC frame exceeds the supported length.");
            }

            int received = await transport.ReadAsync(input.AsMemory(inputCount), cancellationToken);
            if (received == 0)
            {
                throw new EndOfStreamException("The HDLC TCP gateway closed the connection.");
            }

            inputCount += received;
        }
    }

    private async Task WriteFrameAsync(HdlcFrame frame, CancellationToken cancellationToken)
    {
        await writeLock.WaitAsync(cancellationToken);
        try
        {
            await WriteFrameCoreAsync(frame, cancellationToken);
        }
        finally
        {
            writeLock.Release();
        }
    }

    private async Task WriteFrameCoreAsync(HdlcFrame frame, CancellationToken cancellationToken)
    {
        byte[] bytes = codec.EncodeBytes(frame);
        Trace("TX", frame, bytes);
        await transport.WriteAsync(bytes, cancellationToken);
    }

    private void ValidateAddresses(HdlcFrame frame)
    {
        if (frame.Destination != local || frame.Source != remote)
        {
            throw new InvalidDataException("Unexpected HDLC frame addresses.");
        }
    }

    private void Trace(string direction, HdlcFrame frame, ReadOnlySpan<byte> bytes)
    {
        string kind = frame.Control switch
        {
            HdlcControl.Snrm => "SNRM",
            HdlcControl.Ua => "UA",
            HdlcControl.Dm => "DM",
            HdlcControl.Disc => "DISC",
            _ when frame.IsInformation => $"I N(S)={frame.SendSequence} N(R)={frame.ReceiveSequence}",
            _ when (frame.Control & HdlcControl.SupervisoryTypeMask) == HdlcControl.ReceiveReady
                => $"RR N(R)={frame.ReceiveSequence}",
            _ => $"control={frame.Control:X2}"
        };
        bool secret = redactAarq && frame.IsInformation && frame.Information.Length > LlcHeader.Length
            && frame.Information[LlcHeader.Length] is (byte)AcseKind.Aarq or (byte)ApduTag.ActionRequest or (byte)ApduTag.ActionResponse;
        string contents = secret ? "[redacted: authentication data]" : Convert.ToHexString(bytes);
        lock (traceLock)
        {
            output.WriteLine($"[{++messageCount:D4}] {direction} {kind} ({bytes.Length} bytes): {contents}");
        }
    }
}
