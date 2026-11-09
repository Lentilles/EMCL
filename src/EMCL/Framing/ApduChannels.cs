using EMCL.Core.Codecs;
using EMCL.Framing.Hdlc.Frames;
using EMCL.Framing.Wrapper;
using EMCL.Transport.Channels;
using EMCL.Transport.Streams;

namespace EMCL.Framing;

/// <summary>Creates Wrapper or HDLC APDU channels over an application-supplied byte transport.</summary>
public static class ApduChannels
{
    /// <summary>Opens one byte transport and applies the selected framing to create an APDU channel.</summary>
    /// <remarks>The returned channel owns the transport. A failed framing setup disposes the opened transport.</remarks>
    /// <param name="settings">The connection factory, framing choice, and resource limits.</param>
    /// <param name="cancellationToken">Cancels connection or HDLC link establishment.</param>
    /// <returns>A connected APDU channel.</returns>
    public static async Task<IApduChannel> ConnectAsync(ApduChannelSettings settings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(settings.Connection);
        ArgumentNullException.ThrowIfNull(settings.Framing);
        if (settings.Framing is ApduFraming.Hdlc hdlcSettings)
        {
            hdlcSettings.Validate();
        }

        var transport = await settings.Connection.ConnectAsync(cancellationToken).ConfigureAwait(false);
        if (transport is null)
        {
            throw new InvalidOperationException("The byte transport factory returned null.");
        }

        if (settings.Framing is ApduFraming.Hdlc hdlc)
        {
            // HDLC link setup owns and disposes the transport if setup fails.
            return await HdlcApduChannel.ConnectAsync(transport, hdlc, settings.Limits, cancellationToken)
                .ConfigureAwait(false);
        }

        try
        {
            return settings.Framing switch
            {
                ApduFraming.Wrapper wrapper => Wrapper(transport, wrapper, settings.Limits),
                _ => throw new NotSupportedException("Unsupported APDU framing.")
            };
        }
        catch
        {
            await transport.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>Wraps an open byte transport with DLMS Wrapper framing. The returned channel owns the transport.</summary>
    public static IApduChannel Wrapper(IByteTransport transport, ushort localPort, ushort remotePort,
        ProtocolLimits? limits = null) => Wrapper(transport, new ApduFraming.Wrapper(localPort, remotePort), limits);

    private static IApduChannel Wrapper(IByteTransport transport, ApduFraming.Wrapper framing,
        ProtocolLimits? limits) => new FramedApduChannel<WrapperFrame>(transport,
            new WrapperCodec(limits), new WrapperApduAdapter(framing.LocalPort, framing.RemotePort)
            {
                NotificationPort = framing.NotificationPort
            }, limits);

    /// <summary>Establishes an HDLC link over an open byte transport. The returned channel owns the transport.</summary>
    /// <remarks>The channel uses stop-and-wait exchange and requires each outgoing APDU to fit one negotiated information frame.</remarks>
    public static async Task<IApduChannel> ConnectHdlcAsync(IByteTransport transport, HdlcAddress local,
        HdlcAddress remote, TimeSpan linkTimeout, ProtocolLimits? limits = null,
        Action<HdlcFrame, bool>? frameObserver = null, CancellationToken cancellationToken = default)
        => await HdlcApduChannel.ConnectAsync(transport, local, remote, linkTimeout, limits, frameObserver,
            cancellationToken).ConfigureAwait(false);
}
