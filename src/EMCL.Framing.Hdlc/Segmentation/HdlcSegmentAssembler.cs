using System.Collections.Immutable;
using EMCL.Core.Codecs;
using EMCL.Framing.Hdlc.Frames;

namespace EMCL.Framing.Hdlc.Segmentation;

/// <summary>Collects ordered HDLC I-frame segments for one receive direction without implementing acknowledgements or retransmission.</summary>
/// <param name="protocolLimits">Optional resource bounds; null selects validated defaults.</param>
public sealed class HdlcSegmentAssembler(ProtocolLimits? protocolLimits = null)
{
    private readonly ProtocolLimits limits = ProtocolLimits.Resolve(protocolLimits);
    private readonly List<byte> pending = [];
    private HdlcAddress source, destination;
    private byte expected;
    private int count;
    /// <summary>Gets whether an incomplete segmented I-frame message is buffered.</summary>
    public bool HasPending => count != 0;

    /// <summary>Discards buffered segments and resets sequence-tracking state.</summary>
    public void Reset()
    {
        pending.Clear();
        count = 0;
    }

    /// <summary>Accepts a data-frame segment, returning completed information octets only after the final segment.</summary>
    /// <param name="frame">The decoded frame to process.</param>
    /// <returns>The complete information message, or null for control frames and incomplete segmented messages.</returns>
    /// <exception cref="System.IO.InvalidDataException">Segment order, addresses, frame kind, or configured transfer bounds are invalid.</exception>
    public ImmutableArray<byte>? Accept(HdlcFrame frame)
    {
        if (!frame.IsInformation && !frame.IsUnnumberedInformation)
        {
            if (frame.Segmented)
            {
                Reset();
                throw new InvalidDataException("A control frame cannot carry segmented data.");
            }

            return null;
        }

        if (frame.IsUnnumberedInformation)
        {
            if (frame.Segmented || HasPending)
            {
                Reset();
                throw new InvalidDataException("UI cannot participate in segmented I-frame transfer.");
            }

            return frame.Information;
        }

        if (HasPending && (frame.Source != source || frame.Destination != destination || frame.SendSequence != expected))
        {
            Reset();
            throw new InvalidDataException("HDLC segment address or sequence mismatch.");
        }

        if (++count > limits.MaxBlockCount || frame.Information.Length > limits.MaxMessageBytes - pending.Count)
        {
            Reset();
            throw new InvalidDataException("HDLC reassembly limit exceeded.");
        }

        source = frame.Source;
        destination = frame.Destination;
        expected = (byte)((frame.SendSequence + 1) & HdlcFrameFormat.SequenceMask);
        pending.AddRange(frame.Information);
        if (frame.Segmented)
        {
            return null;
        }

        var result = pending.ToImmutableArray();
        Reset();
        return result;
    }
}
