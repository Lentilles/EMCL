using System.IO.Ports;
using System.Text.Json;
using System.Text.Json.Nodes;
using EMCL.Examples.AssociationDump.Configuration;
using EMCL.Transport.Streams;

namespace EMCL.Tests.Examples.AssociationDump;

public sealed class SerialConfigurationTests
{
    [Fact]
    public async Task LoadsSerialLineSettingsAndCreatesSerialFactoryWithHdlc()
    {
        using var directory = new ExportTestDirectory();
        var settings = ExportTestData.Configuration("association.json");
        var serial = new SerialConnectionSettings
        {
            PortName = "COM3",
            BaudRate = 19200,
            DataBits = 7,
            Parity = Parity.Even,
            StopBits = StopBits.Two,
            Handshake = Handshake.XOnXOff,
            DtrEnable = true,
            RtsEnable = true,
            ConnectTimeoutSeconds = 3
        };
        settings = settings with
        {
            Channel = new()
            {
                Connection = serial,
                Framing = new HdlcFramingSettings()
            }
        };
        string path = Path.Combine(directory.Path, "meter.json");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(settings, ConfigurationFileTests.JsonOptions),
            TestContext.Current.CancellationToken);
        var loaded = await ConfigurationFile.LoadAsync(path, TestContext.Current.CancellationToken);
        Assert.Equal(serial, Assert.IsType<SerialConnectionSettings>(loaded.Settings.Channel.Connection));
        var factory = Assert.IsType<SerialByteTransportFactory>(loaded.CreateChannelSettings().Connection);
        Assert.Equal(serial.PortSettings, factory.Settings);
        Assert.Equal(TimeSpan.FromSeconds(3), factory.Timeout);
        Assert.IsType<EMCL.Framing.ApduFraming.Hdlc>(loaded.CreateChannelSettings().Framing);

        var document = ExportTestData.Document(settings, 1, 1);
        Assert.Equal("HdlcSerial", document.Source.Transport);
        Assert.Null(document.Source.Host);
        Assert.Null(document.Source.TcpPort);
        Assert.Equal("COM3", document.Source.Serial!.PortName);
        Assert.Equal("Even", document.Source.Serial.Parity);
        Assert.Equal(19200, document.Source.Serial.BaudRate);
    }

    [Theory]
    [InlineData("\"kind\":\"Serial\",\"portName\":\"COM3\",\"tcpPort\":4059")]
    [InlineData("\"kind\":\"Tcp\",\"host\":\"localhost\",\"tcpPort\":4059,\"baudRate\":9600")]
    [InlineData("\"kind\":\"Unknown\"")]
    [InlineData("\"kind\":\"Serial\"")]
    [InlineData("\"kind\":\"Serial\",\"portName\":\"COM3\",\"parity\":99")]
    [InlineData("\"kind\":\"Serial\",\"portName\":\"COM3\",\"stopBits\":\"Invalid\"")]
    public async Task RejectsUnknownOrMixedConnectionSettings(string fields)
    {
        using var directory = new ExportTestDirectory();
        var json = JsonSerializer.SerializeToNode(ExportTestData.Configuration("association.json"),
            ConfigurationFileTests.JsonOptions)!;
        json["channel"]!["connection"] = JsonNode.Parse("{" + fields + "}");
        string path = Path.Combine(directory.Path, "meter.json");
        await File.WriteAllTextAsync(path, json.ToJsonString(), TestContext.Current.CancellationToken);
        await Assert.ThrowsAsync<JsonException>(async () =>
            await ConfigurationFile.LoadAsync(path, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task BundledSerialTemplateLoadsWithoutOpeningPort()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "meter.serial.example.json");
        var loaded = await ConfigurationFile.LoadAsync(path, TestContext.Current.CancellationToken);
        var serial = Assert.IsType<SerialConnectionSettings>(loaded.Settings.Channel.Connection);
        Assert.Equal(9600, serial.BaudRate);
        Assert.IsType<SerialByteTransportFactory>(loaded.CreateChannelSettings().Connection);
    }
}
