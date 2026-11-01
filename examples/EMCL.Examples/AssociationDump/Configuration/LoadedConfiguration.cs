using EMCL.Client.Sessions;
using EMCL.Core.Objects;
using EMCL.Framing;
using EMCL.Framing.Hdlc.Frames;
using EMCL.Transport.Streams;

namespace EMCL.Examples.AssociationDump.Configuration;

internal sealed record LoadedConfiguration(MeterConfiguration Settings, ClientSessionOptions Session,
    LogicalName AssociationName, TimeSpan ConnectTimeout, string OutputPath)
{
    internal ApduChannelSettings CreateChannelSettings(Action<HdlcFrame, bool>? frameObserver = null)
    {
        var channel = Settings.Channel;
        ApduFraming framing = channel.Framing switch
        {
            WrapperFramingSettings wrapper => new ApduFraming.Wrapper(wrapper.ClientPort, wrapper.ServerPort),
            HdlcFramingSettings hdlc => new ApduFraming.Hdlc(
                HdlcAddress.Client(hdlc.ClientAddress),
                HdlcAddress.Server(hdlc.ServerLogicalAddress, hdlc.ServerPhysicalAddress,
                    hdlc.ExtendedAddress), Session.RequestTimeout, frameObserver),
            _ => throw new ArgumentException("Unsupported channel framing.")
        };
        return new(new TcpByteTransportFactory(channel.Connection.Host, channel.Connection.TcpPort,
            ConnectTimeout), framing, Settings.Limits);
    }
}
