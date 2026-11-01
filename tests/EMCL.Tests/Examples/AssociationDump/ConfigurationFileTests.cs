using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using EMCL.Client.Association;
using EMCL.Examples.AssociationDump.Configuration;
using EMCL.Framing;
using EMCL.Framing.Hdlc.Frames;
using EMCL.Protocol.Apdu;

namespace EMCL.Tests.Examples.AssociationDump;

public sealed class ConfigurationFileTests
{
    internal static JsonSerializerOptions JsonOptions
    {
        get;
    } = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) }
    };

    [Fact]
    public async Task BundledTemplateLoadsAsOneChannelConfiguration()
    {
        string path = Path.Combine(AppContext.BaseDirectory, DefaultConfigurationFile.ExampleFileName);
        var loaded = await ConfigurationFile.LoadAsync(path, TestContext.Current.CancellationToken);
        Assert.IsType<WrapperFramingSettings>(loaded.Settings.Channel.Framing);
        Assert.Equal("127.0.0.1", Assert.IsType<TcpConnectionSettings>(loaded.Settings.Channel.Connection).Host);
    }

    [Fact]
    public async Task LoadsAllConnectionParametersAndResolvesOutputRelativeToConfiguration()
    {
        using var directory = new ExportTestDirectory();
        var settings = ExportTestData.Configuration("exports/association.json");
        string path = Path.Combine(directory.Path, "meter.json");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(settings, JsonOptions), TestContext.Current.CancellationToken);
        var loaded = await ConfigurationFile.LoadAsync(path, TestContext.Current.CancellationToken);
        Assert.Equal(Path.Combine(directory.Path, "exports", "association.json"), loaded.OutputPath);
        var expected = Assert.IsType<WrapperFramingSettings>(settings.Channel.Framing);
        var actual = Assert.IsType<WrapperFramingSettings>(loaded.Settings.Channel.Framing);
        Assert.Equal(expected.ClientPort, actual.ClientPort);
        Assert.Equal(expected.ServerPort, actual.ServerPort);
        Assert.Equal(settings.Association.Version, loaded.Settings.Association.Version);
        Assert.Equal(settings.Association.Conformance, loaded.Session.Association.Conformance);
        Assert.Equal(TimeSpan.FromSeconds(settings.Association.RequestTimeoutSeconds), loaded.Session.RequestTimeout);
    }

    [Theory]
    [InlineData("621EDF")]
    [InlineData("0x621EDF")]
    public void LoadsFullAdvertisedConformanceWithoutEnablingUnsupportedOperations(string value)
    {
        var original = ExportTestData.Configuration("unused.json");
        var settings = original with
        {
            Association = original.Association with
            {
                AdvertisedConformanceHex = value
            }
        };

        var loaded = ConfigurationFile.Validate(settings, Path.GetTempPath());
        Assert.Equal((DlmsConformance)0x621EDF, loaded.Session.Association.AdvertisedConformance);
        Assert.Equal(settings.Association.Conformance, loaded.Session.Association.Conformance);
    }

    [Theory]
    [InlineData(false, 2)]
    [InlineData(true, 4)]
    public void AcceptsHdlcGatewayAddressesAndRejectsExtendedAddressWithoutPhysicalDevice(
        bool extendedAddress, byte expectedAddressLength)
    {
        var settings = ExportTestData.Configuration("unused.json");
        settings = settings with
        {
            Channel = settings.Channel with
            {
                Framing = new HdlcFramingSettings
                {
                    ClientAddress = 48,
                    ServerLogicalAddress = 1,
                    ServerPhysicalAddress = 17,
                    ExtendedAddress = extendedAddress
                }
            }
        };

        var loaded = ConfigurationFile.Validate(settings, Path.GetTempPath());
        var framing = Assert.IsType<HdlcFramingSettings>(loaded.Settings.Channel.Framing);
        Assert.Equal((byte)48, framing.ClientAddress);
        Assert.Equal((ushort)17, framing.ServerPhysicalAddress);
        var remote = Assert.IsType<ApduFraming.Hdlc>(loaded.CreateChannelSettings().Framing).Remote;
        Assert.Equal(HdlcAddress.Server(1, 17, extended: extendedAddress), remote);
        Assert.Equal(expectedAddressLength, remote.Length);

        var invalid = settings with
        {
            Channel = settings.Channel with
            {
                Framing = framing with
                {
                    ServerPhysicalAddress = null,
                    ExtendedAddress = true
                }
            }
        };
        Assert.Throws<ArgumentException>(() => ConfigurationFile.Validate(invalid, Path.GetTempPath()));
    }

    [Fact]
    public async Task UpgradesLegacyConnectionSettingsAndRejectsFieldsFromAnotherFraming()
    {
        using var directory = new ExportTestDirectory();
        string path = Path.Combine(directory.Path, "meter.json");
        var modern = ExportTestData.Configuration("association.json");
        var legacy = JsonSerializer.SerializeToNode(modern, JsonOptions)!.AsObject();
        legacy.Remove("channel");
        legacy["connection"] = JsonNode.Parse("""
            {"transport":"HdlcTcp","host":"127.0.0.1","tcpPort":4059,
             "hdlcClientAddress":48,"hdlcServerLogicalAddress":1,"hdlcServerPhysicalAddress":17,
             "hdlcExtendedAddress":true}
            """);
        await File.WriteAllTextAsync(path, legacy.ToJsonString(), TestContext.Current.CancellationToken);
        var loaded = await ConfigurationFile.LoadAsync(path, TestContext.Current.CancellationToken);
        var hdlc = Assert.IsType<HdlcFramingSettings>(loaded.Settings.Channel.Framing);
        Assert.Equal((byte)48, hdlc.ClientAddress);
        Assert.Equal((ushort)17, hdlc.ServerPhysicalAddress);
        Assert.Equal(HdlcAddress.Server(1, 17, extended: true),
            Assert.IsType<ApduFraming.Hdlc>(loaded.CreateChannelSettings().Framing).Remote);

        var invalid = JsonSerializer.SerializeToNode(modern, JsonOptions)!.AsObject();
        invalid["channel"]!["framing"]!["serverLogicalAddress"] = 1;
        await File.WriteAllTextAsync(path, invalid.ToJsonString(), TestContext.Current.CancellationToken);
        await Assert.ThrowsAsync<JsonException>(async () => await ConfigurationFile.LoadAsync(path,
            TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("password", "test-password")]
    [InlineData("passwordHex", "746573742D70617373776F7264")]
    [InlineData("environment", "test-password")]
    public void LoadsLowAuthenticationFromOneExplicitPasswordSource(string source, string value)
    {
        string variable = $"EMCL_TEST_PASSWORD_{Guid.NewGuid():N}";
        try
        {
            var settings = ExportTestData.Configuration("unused.json");
            settings = settings with
            {
                Association = settings.Association with
                {
                    Authentication = ClientAuthentication.Low,
                    Password = source == "password" ? value : null,
                    PasswordHex = source == "passwordHex" ? value : null,
                    PasswordEnvironmentVariable = source == "environment" ? variable : null
                }
            };
            if (source == "environment")
            {
                Environment.SetEnvironmentVariable(variable, value);
            }

            var loaded = ConfigurationFile.Validate(settings, Path.GetTempPath());
            Assert.Equal("test-password", Encoding.UTF8.GetString(loaded.Session.Association.Password.AsSpan()));
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, null);
        }
    }

    [Fact]
    public void LoadsHighAuthenticationAndRequiresActionConformance()
    {
        var settings = ExportTestData.Configuration("unused.json");
        settings = settings with
        {
            Association = settings.Association with
            {
                Authentication = ClientAuthentication.High,
                Password = "0000000100000001",
                Conformance = DlmsConformance.Get | DlmsConformance.Action
            }
        };

        var loaded = ConfigurationFile.Validate(settings, Path.GetTempPath());
        Assert.Equal("0000000100000001", Encoding.ASCII.GetString(loaded.Session.Association.Password.AsSpan()));
        Assert.Throws<ArgumentException>(() => ConfigurationFile.Validate(settings with
        {
            Association = settings.Association with
            {
                Conformance = DlmsConformance.Get
            }
        }, Path.GetTempPath()));
    }

    [Fact]
    public async Task RejectsUnknownConfigurationFieldsAndProtectsConfigurationFromOverwrite()
    {
        using var directory = new ExportTestDirectory();
        string path = Path.Combine(directory.Path, "meter.json");
        var settings = ExportTestData.Configuration(path);
        var json = JsonSerializer.SerializeToNode(settings, JsonOptions)!.AsObject();
        json["misspelledSetting"] = true;
        await File.WriteAllTextAsync(path, json.ToJsonString(), TestContext.Current.CancellationToken);
        await Assert.ThrowsAsync<JsonException>(async () => await ConfigurationFile.LoadAsync(path, TestContext.Current.CancellationToken));
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(settings, JsonOptions), TestContext.Current.CancellationToken);
        await Assert.ThrowsAsync<ArgumentException>(async () => await ConfigurationFile.LoadAsync(path, TestContext.Current.CancellationToken));
    }

    [Fact]
    public void RejectsIncompatibleVersionAndPasswordSettingsBeforeConnecting()
    {
        var valid = ExportTestData.Configuration("unused.json");
        var invalid = new[]
        {
            valid with { Channel = valid.Channel with { Connection = new TcpConnectionSettings { Host = "127.0.0.1", TcpPort = 0 } } },
            valid with { Association = valid.Association with { Version = 2 } },
            valid with { Association = valid.Association with { Password = "must-not-be-printed" } },
            valid with { Association = valid.Association with { Authentication = ClientAuthentication.Low, Password = "x", PasswordHex = "78" } },
            valid with { Association = valid.Association with { Authentication = ClientAuthentication.Low, PasswordHex = "must-not-be-printed" } },
            valid with { Association = valid.Association with { RequestTimeoutSeconds = -1 } }
        };
        foreach (var settings in invalid)
        {
            var error = Assert.Throws<ArgumentException>(() => ConfigurationFile.Validate(settings, Path.GetTempPath()));
            Assert.DoesNotContain("must-not-be-printed", error.Message, StringComparison.Ordinal);
        }
    }
}
