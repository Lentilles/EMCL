using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using EMCL.Client.Association;
using EMCL.Examples.AssociationDump.Configuration;
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
    public async Task LoadsAllConnectionParametersAndResolvesOutputRelativeToConfiguration()
    {
        using var directory = new ExportTestDirectory();
        var settings = ExportTestData.Configuration("exports/association.json");
        string path = Path.Combine(directory.Path, "meter.json");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(settings, JsonOptions), TestContext.Current.CancellationToken);
        var loaded = await ConfigurationFile.LoadAsync(path, TestContext.Current.CancellationToken);
        Assert.Equal(Path.Combine(directory.Path, "exports", "association.json"), loaded.OutputPath);
        Assert.Equal(settings.Connection.ClientWrapperPort, loaded.Settings.Connection.ClientWrapperPort);
        Assert.Equal(settings.Connection.ServerWrapperPort, loaded.Settings.Connection.ServerWrapperPort);
        Assert.Equal(settings.Association.Version, loaded.Settings.Association.Version);
        Assert.Equal(settings.Association.Conformance, loaded.Session.Association.Conformance);
        Assert.Equal(TimeSpan.FromSeconds(settings.Association.RequestTimeoutSeconds), loaded.Session.RequestTimeout);
    }

    [Fact]
    public void AcceptsHdlcGatewayAddressesAndRejectsExtendedAddressWithoutPhysicalDevice()
    {
        var settings = ExportTestData.Configuration("unused.json");
        settings = settings with
        {
            Connection = settings.Connection with
            {
                Transport = MeterTransport.HdlcTcp,
                HdlcClientAddress = 48,
                HdlcServerLogicalAddress = 1,
                HdlcServerPhysicalAddress = 17
            }
        };

        var loaded = ConfigurationFile.Validate(settings, Path.GetTempPath());
        Assert.Equal(MeterTransport.HdlcTcp, loaded.Settings.Connection.Transport);
        Assert.Equal((byte)48, loaded.Settings.Connection.HdlcClientAddress);
        Assert.Equal((ushort)17, loaded.Settings.Connection.HdlcServerPhysicalAddress);

        var invalid = settings with
        {
            Connection = settings.Connection with
            {
                HdlcServerPhysicalAddress = null,
                HdlcExtendedAddress = true
            }
        };
        Assert.Throws<ArgumentException>(() => ConfigurationFile.Validate(invalid, Path.GetTempPath()));
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
    public void RejectsIncompatibleProfileVersionAndPasswordSettingsBeforeConnecting()
    {
        var valid = ExportTestData.Configuration("unused.json");
        var invalid = new[]
        {
            valid with { Connection = valid.Connection with { TcpPort = 0 } },
            valid with { Association = valid.Association with { ProfileId = "unknown" } },
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
