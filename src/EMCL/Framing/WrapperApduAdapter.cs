using System.Collections.Immutable;
using EMCL.Framing.Wrapper;
using EMCL.Protocol.Apdu;
using EMCL.Transport.Channels;

namespace EMCL.Framing;

/// <summary>Maps complete Wrapper payloads to APDUs and optionally validates the source and destination wPorts.</summary>
/// <param name="localPort">The local Wrapper wPort.</param>
/// <param name="remotePort">The expected remote Wrapper wPort.</param>
/// <param name="validatePorts">Whether to reject frames whose source or destination wPorts do not match.</param>
public sealed class WrapperApduAdapter(ushort localPort, ushort remotePort, bool validatePorts = true) : IFramePayloadAdapter<WrapperFrame>
{
    internal ushort? NotificationPort
    {
        get; init;
    }

    /// <inheritdoc />
    public bool HasPending => false;

    /// <inheritdoc />
    public ReadOnlyMemory<byte>? Accept(WrapperFrame frame)
    {
        if (validatePorts && (frame.SourcePort != remotePort
            || (frame.DestinationPort != localPort && !IsAddressedNotification(frame))))
        {
            throw new InvalidDataException("Unexpected Wrapper ports.");
        }

        return frame.Payload.ToArray();
    }

    private bool IsAddressedNotification(WrapperFrame frame) =>
        frame.DestinationPort == NotificationPort
        && !frame.Payload.IsEmpty && (ApduTag)frame.Payload[0] is ApduTag.DataNotification
            or ApduTag.GeneralGlobalCiphering or ApduTag.GeneralDedicatedCiphering or ApduTag.GeneralBlockTransfer;

    /// <inheritdoc />
    public IEnumerable<WrapperFrame> Frame(ReadOnlyMemory<byte> apdu) => [new(localPort, remotePort, apdu.ToArray().ToImmutableArray())];
    /// <inheritdoc />
    public bool IsIdle(ReadOnlySpan<byte> buffered) => buffered.IsEmpty;
}
