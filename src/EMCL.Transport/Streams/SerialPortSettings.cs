using System.IO.Ports;

namespace EMCL.Transport.Streams;

/// <summary>Configures a binary serial connection without changing the port after opening.</summary>
/// <param name="PortName">Operating-system port name, such as COM3 or /dev/ttyUSB0.</param>
/// <param name="BaudRate">Serial line speed in bits per second.</param>
/// <param name="DataBits">Number of data bits, from five through eight.</param>
/// <param name="Parity">Parity checking mode.</param>
/// <param name="StopBits">Number of stop bits; None is not supported.</param>
/// <param name="Handshake">Hardware or software flow control.</param>
/// <param name="DtrEnable">Whether to assert the DTR line.</param>
/// <param name="RtsEnable">Whether to assert RTS when hardware flow control is disabled.</param>
public sealed record SerialPortSettings(string PortName, int BaudRate = 9600, int DataBits = 8,
    Parity Parity = Parity.None, StopBits StopBits = StopBits.One, Handshake Handshake = Handshake.None,
    bool DtrEnable = false, bool RtsEnable = false)
{
    /// <summary>Checks settings before any attempt to access the port.</summary>
    public void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(PortName);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(BaudRate);
        if (DataBits is < 5 or > 8)
        {
            throw new ArgumentOutOfRangeException(nameof(DataBits));
        }

        if (!Enum.IsDefined(Parity))
        {
            throw new ArgumentOutOfRangeException(nameof(Parity));
        }

        if (!Enum.IsDefined(StopBits) || StopBits == StopBits.None)
        {
            throw new ArgumentOutOfRangeException(nameof(StopBits));
        }

        if (!Enum.IsDefined(Handshake))
        {
            throw new ArgumentOutOfRangeException(nameof(Handshake));
        }

        if (RtsEnable && Handshake is Handshake.RequestToSend or Handshake.RequestToSendXOnXOff)
        {
            throw new ArgumentException("RTS is controlled by the selected hardware handshake.", nameof(RtsEnable));
        }
    }

    internal SerialPort CreatePort()
    {
        Validate();
        return new(PortName, BaudRate, Parity, DataBits, StopBits)
        {
            Handshake = Handshake,
            DtrEnable = DtrEnable,
            RtsEnable = RtsEnable,
            // DLMS carries binary data: do not substitute parity-error bytes or discard NULs.
            ParityReplace = 0,
            DiscardNull = false
        };
    }
}
