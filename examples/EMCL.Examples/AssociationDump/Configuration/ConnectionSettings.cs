using System.Text.Json;
using System.Text.Json.Serialization;
using EMCL.Transport.Streams;

namespace EMCL.Examples.AssociationDump.Configuration;

[JsonConverter(typeof(ConnectionSettingsConverter))]
internal abstract record ConnectionSettings
{
    public double ConnectTimeoutSeconds { get; init; } = 10;
    internal abstract string Kind
    {
        get;
    }
    internal abstract string Endpoint
    {
        get;
    }
    internal abstract void Validate();
    internal abstract IByteTransportFactory CreateFactory(TimeSpan timeout);
}

// A missing discriminator keeps existing TCP configuration files valid.
internal sealed class ConnectionSettingsConverter : JsonConverter<ConnectionSettings>
{
    public override ConnectionSettings Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException("channel.connection must be an object.");
        }

        string? kind = "Tcp";
        if (document.RootElement.TryGetProperty("kind", out var discriminator))
        {
            if (discriminator.ValueKind != JsonValueKind.String)
            {
                throw new JsonException("channel.connection.kind must be a string.");
            }

            kind = discriminator.GetString();
        }
        // Remove the discriminator before strict deserialization of the concrete settings.
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (property.Name != "kind")
                {
                    property.WriteTo(writer);
                }
            }

            writer.WriteEndObject();
        }

        return kind switch
        {
            "Tcp" => JsonSerializer.Deserialize<TcpConnectionSettings>(buffer.ToArray(), options)!,
            "Serial" => JsonSerializer.Deserialize<SerialConnectionSettings>(buffer.ToArray(), options)!,
            _ => throw new JsonException("channel.connection.kind must be Tcp or Serial.")
        };
    }

    public override void Write(Utf8JsonWriter writer, ConnectionSettings value, JsonSerializerOptions options)
    {
        var settings = JsonSerializer.SerializeToElement(value, value.GetType(), options);
        writer.WriteStartObject();
        writer.WriteString("kind", value.Kind);
        foreach (var property in settings.EnumerateObject())
        {
            property.WriteTo(writer);
        }

        writer.WriteEndObject();
    }
}
