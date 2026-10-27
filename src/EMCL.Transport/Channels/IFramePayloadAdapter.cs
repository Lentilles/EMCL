namespace EMCL.Transport.Channels;

/// <summary>Converts decoded frames into complete APDUs and splits outgoing APDUs into transport frames.</summary>
/// <typeparam name="TFrame">The transport frame representation.</typeparam>
public interface IFramePayloadAdapter<TFrame>
{
    /// <summary>Processes one frame, returning a complete APDU or null for control frames and pending segments.</summary>
    /// <param name="frame">The decoded frame to process.</param>
    /// <returns>The completed APDU, or null while no application message is ready.</returns>
    ReadOnlyMemory<byte>? Accept(TFrame frame);
    /// <summary>Builds the frame sequence carrying one outgoing APDU.</summary>
    /// <param name="apdu">One complete application PDU.</param>
    /// <returns>The ordered outgoing frames.</returns>
    IEnumerable<TFrame> Frame(ReadOnlyMemory<byte> apdu);
    /// <summary>Determines whether buffered octets contain only ignorable idle data or a reusable boundary.</summary>
    /// <param name="buffered">The currently buffered frame-input octets.</param>
    /// <returns>True when all supplied octets represent idle framing state.</returns>
    bool IsIdle(ReadOnlySpan<byte> buffered);
    /// <summary>Gets how many trailing idle octets must be retained for the next frame; the default is zero.</summary>
    int IdleBytesToRetain => 0;

    /// <summary>Gets whether the adapter holds an incomplete segmented message.</summary>
    bool HasPending
    {
        get;
    }
}
