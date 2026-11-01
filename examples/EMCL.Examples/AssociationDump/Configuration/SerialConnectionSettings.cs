using System.IO.Ports;
using EMCL.Transport.Streams;

namespace EMCL.Examples.AssociationDump.Configuration;

internal sealed record SerialConnectionSettings : ConnectionSettings
{
    public required string PortName
    {
        get; init;
    }
    public int BaudRate { get; init; } = 9600;
    public int DataBits { get; init; } = 8;
    public Parity Parity { get; init; } = Parity.None;
    public StopBits StopBits { get; init; } = StopBits.One;
    public Handshake Handshake { get; init; } = Handshake.None;
    public bool DtrEnable
    {
        get; init;
    }
    public bool RtsEnable
    {
        get; init;
    }

    internal override string Kind => "Serial";
    internal override string Endpoint => $"{PortName} ({BaudRate} baud, {DataBits} data bits, {Parity}, {StopBits} stop bits)";
    internal SerialPortSettings PortSettings => new(PortName, BaudRate, DataBits, Parity, StopBits, Handshake,
        DtrEnable, RtsEnable);
    internal override void Validate() => PortSettings.Validate();
    internal override IByteTransportFactory CreateFactory(TimeSpan timeout) => new SerialByteTransportFactory(PortSettings, timeout);
}
