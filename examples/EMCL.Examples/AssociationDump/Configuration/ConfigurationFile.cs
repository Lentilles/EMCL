using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using EMCL.Client.Association;
using EMCL.Client.Sessions;
using EMCL.Core.Objects;
using EMCL.Framing.Hdlc.Frames;
using EMCL.Objects.Definitions;
using EMCL.Protocol.Apdu;

namespace EMCL.Examples.AssociationDump.Configuration;

internal static class ConfigurationFile
{
    private const int DefaultNotificationCapacity = 64;
    private const double MaximumTimeoutMilliseconds = uint.MaxValue - 1;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters =
        {
            new JsonStringEnumConverter<ClientAuthentication>(allowIntegerValues: false),
            new JsonStringEnumConverter<DlmsConformance>(allowIntegerValues: false),
            new JsonStringEnumConverter<MeterTransport>(allowIntegerValues: false)
        }
    };

    internal static async Task<LoadedConfiguration> LoadAsync(string path, CancellationToken cancellationToken)
    {
        string configurationPath = Path.GetFullPath(path);
        await using var file = File.OpenRead(configurationPath);
        using var document = await JsonDocument.ParseAsync(file, cancellationToken: cancellationToken);
        var settings = document.RootElement.TryGetProperty("connection", out _)
            ? document.RootElement.Deserialize<LegacyMeterConfiguration>(JsonOptions)?.Upgrade()
            : document.RootElement.Deserialize<MeterConfiguration>(JsonOptions);
        if (settings is null)
        {
            throw new ArgumentException("The configuration must contain a JSON object.");
        }
        var loaded = Validate(settings, Path.GetDirectoryName(configurationPath)!);
        if (string.Equals(configurationPath, loaded.OutputPath, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("The output file must differ from the configuration file.");
        }

        return loaded;
    }

    internal static LoadedConfiguration Validate(MeterConfiguration settings, string directory)
    {
        ArgumentNullException.ThrowIfNull(settings.Channel);
        ArgumentNullException.ThrowIfNull(settings.Channel.Connection);
        ArgumentNullException.ThrowIfNull(settings.Channel.Framing);
        ArgumentNullException.ThrowIfNull(settings.Association);
        ArgumentNullException.ThrowIfNull(settings.Output);
        ArgumentNullException.ThrowIfNull(settings.Limits);
        settings.Limits.Validate();
        var connection = settings.Channel.Connection;
        ArgumentException.ThrowIfNullOrWhiteSpace(connection.Host);
        if (connection.TcpPort is <= 0 or > ushort.MaxValue)
        {
            throw new ArgumentException("channel.connection.tcpPort must be a valid nonzero TCP port.");
        }

        switch (settings.Channel.Framing)
        {
            case WrapperFramingSettings wrapper:
                if (wrapper.ClientPort == 0 || wrapper.ServerPort == 0)
                {
                    throw new ArgumentException("Wrapper source and destination ports must be nonzero.");
                }

                break;
            case HdlcFramingSettings hdlc:
                _ = HdlcAddress.Client(hdlc.ClientAddress);
                _ = HdlcAddress.Server(hdlc.ServerLogicalAddress, hdlc.ServerPhysicalAddress,
                    hdlc.ExtendedAddress);
                if (hdlc.ExtendedAddress && !hdlc.ServerPhysicalAddress.HasValue)
                {
                    throw new ArgumentException("channel.framing.extendedAddress requires serverPhysicalAddress.");
                }

                break;
            default:
                throw new ArgumentException("channel.framing.kind must be Wrapper or Hdlc.");
        }

        var association = settings.Association;
        if (association.Version is not (AssociationLnMembers.Version1 or AssociationLnMembers.Version3))
        {
            throw new ArgumentException("association.version must explicitly select Association LN version 1 or 3.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(association.LogicalDeviceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(association.AssociationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(association.LogicalName);
        var name = LogicalName.Parse(association.LogicalName);
        if ((association.Conformance & DlmsConformance.Get) == DlmsConformance.None
            || (association.Conformance & ~LnAssociationOptions.SupportedConformance) != DlmsConformance.None)
        {
            throw new ArgumentException("association.conformance must include Get and only implemented conformance flags.");
        }

        if (association.MaxReceivePduSize != DlmsNegotiation.UnlimitedPduSize
            && (association.MaxReceivePduSize < DlmsNegotiation.MinimumPduSize
                || association.MaxReceivePduSize > settings.Limits.MaxMessageBytes))
        {
            throw new ArgumentException("association.maxReceivePduSize is reserved or exceeds limits.maxMessageBytes.");
        }

        if (association.Authentication == ClientAuthentication.High
            && (association.Conformance & DlmsConformance.Action) == DlmsConformance.None)
        {
            throw new ArgumentException("association.conformance must include Action for High authentication.");
        }

        DlmsConformance? advertisedConformance = ParseAdvertisedConformance(association.AdvertisedConformanceHex);
        if (advertisedConformance is { } advertised
            && (association.Conformance & ~advertised) != DlmsConformance.None)
        {
            throw new ArgumentException("association.advertisedConformanceHex must include every service in association.conformance.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(settings.Output.Path);
        var outputPath = Path.GetFullPath(settings.Output.Path, directory);
        if (Directory.Exists(outputPath))
        {
            throw new ArgumentException("output.path must name a file, not a directory.");
        }

        if (!settings.Output.Overwrite && File.Exists(outputPath))
        {
            throw new IOException("The output file already exists; choose another path or enable output.overwrite.");
        }

        var password = Password(association);
        if (association.Authentication == ClientAuthentication.High && password.Length != LnAssociationOptions.HighSecretLength)
        {
            throw new ArgumentException($"High authentication requires a {LnAssociationOptions.HighSecretLength}-byte password.");
        }

        var session = new ClientSessionOptions
        {
            Limits = settings.Limits,
            RequestTimeout = Timeout(association.RequestTimeoutSeconds, "association.requestTimeoutSeconds"),
            NotificationCapacity = Math.Min(DefaultNotificationCapacity, settings.Limits.MaxCollectionItems),
            Association = new()
            {
                Conformance = association.Conformance,
                AdvertisedConformance = advertisedConformance,
                MaxReceivePduSize = association.MaxReceivePduSize,
                Authentication = association.Authentication,
                Password = password
            }
        };
        return new(settings, session, name, Timeout(connection.ConnectTimeoutSeconds,
            "channel.connection.connectTimeoutSeconds"), outputPath);
    }

    private static TimeSpan Timeout(double seconds, string parameter)
    {
        if (!double.IsFinite(seconds) || seconds <= 0 || seconds * TimeSpan.MillisecondsPerSecond > MaximumTimeoutMilliseconds)
        {
            throw new ArgumentException($"{parameter} must contain a positive, supported number of seconds.");
        }

        return TimeSpan.FromSeconds(seconds);
    }

    private static DlmsConformance? ParseAdvertisedConformance(string? value)
    {
        if (value is null)
        {
            return null;
        }

        ReadOnlySpan<char> hex = value.AsSpan().Trim();
        if (hex.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            hex = hex[2..];
        }

        if (hex.IsEmpty || hex.Length > 6
            || !uint.TryParse(hex, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out uint bits))
        {
            throw new ArgumentException("association.advertisedConformanceHex must be a 1–6 digit hexadecimal value.");
        }

        return (DlmsConformance)bits;
    }

    private static ImmutableArray<byte> Password(AssociationSettings settings)
    {
        int supplied = (settings.Password != null ? 1 : 0) + (settings.PasswordHex != null ? 1 : 0)
            + (settings.PasswordEnvironmentVariable != null ? 1 : 0);
        if (settings.Authentication == ClientAuthentication.None)
        {
            if (supplied != 0)
            {
                throw new ArgumentException("Password settings require Low or High authentication.");
            }

            return [];
        }

        if (settings.Authentication is not (ClientAuthentication.Low or ClientAuthentication.High) || supplied != 1)
        {
            throw new ArgumentException("Low or High authentication requires exactly one of password, passwordHex, or passwordEnvironmentVariable.");
        }

        if (settings.PasswordHex != null)
        {
            try
            {
                return Convert.FromHexString(settings.PasswordHex).ToImmutableArray();
            }
            catch (FormatException)
            {
                throw new ArgumentException("association.passwordHex must contain an even number of hexadecimal digits.");
            }
        }

        string? password = settings.Password;
        if (settings.PasswordEnvironmentVariable != null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(settings.PasswordEnvironmentVariable);
            password = Environment.GetEnvironmentVariable(settings.PasswordEnvironmentVariable)
                ?? throw new ArgumentException("The configured password environment variable is not set.");
        }

        return Encoding.UTF8.GetBytes(password!).ToImmutableArray();
    }
}
