using System.Net.Sockets;
using System.Net;
using System.Text.Json;
using EMCL.Core.Codecs;
using EMCL.Core.Values;
using EMCL.Examples.AssociationDump;
using EMCL.Examples.AssociationDump.Configuration;
using EMCL.Framing;
using EMCL.Objects.Definitions;
using EMCL.Protocol.Acse;
using EMCL.Protocol.Apdu;
using EMCL.Protocol.Axdr;
using EMCL.Transport.Channels;
using EMCL.Transport.Streams;

namespace EMCL.Tests.Examples.AssociationDump;

public sealed class AssociationDumpCommandTests
{
    [Theory]
    [InlineData(3, false)]
    [InlineData(1, false)]
    [InlineData(3, true)]
    public async Task ConnectsOverTcpReadsCatalogBlocksAndWritesJson(byte version, bool closeAfterData)
    {
        using var directory = new ExportTestDirectory();
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var settings = ExportTestData.Configuration("association.json");
        settings = settings with
        {
            Channel = settings.Channel with
            {
                Connection = settings.Channel.Connection with
                {
                    TcpPort = ((IPEndPoint)listener.LocalEndpoint).Port
                }
            },
            Association = settings.Association with
            {
                                Version = version,
                RequestTimeoutSeconds = 3
            }
        };
        string configurationPath = Path.Combine(directory.Path, DefaultConfigurationFile.LocalFileName);
        await File.WriteAllTextAsync(configurationPath, JsonSerializer.Serialize(settings, ConfigurationFileTests.JsonOptions),
            TestContext.Current.CancellationToken);
        using var output = new StringWriter();
        using var error = new StringWriter();
        var server = ServeAsync();
        await AssociationDumpCommand.RunAsync(DefaultConfigurationFile.Resolve(directory.Path), output, error,
            TestContext.Current.CancellationToken);
        await server;
        string outputPath = Path.Combine(directory.Path, "association.json");
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(outputPath, TestContext.Current.CancellationToken));
        Assert.Equal(2, document.RootElement.GetProperty("objectCount").GetInt32());
        Assert.Equal(version, document.RootElement.GetProperty("source").GetProperty("associationVersion").GetInt32());
        var register = document.RootElement.GetProperty("objects").EnumerateArray()
            .Single(obj => obj.GetProperty("classId").GetInt32() == 3);
        Assert.Equal("1.0.1.8.0.255", register.GetProperty("obisCode").GetString());
        var access = register.GetProperty("attributes").EnumerateArray()
            .Single(attribute => attribute.GetProperty("index").GetInt32() == 2).GetProperty("access");
        Assert.True(access.GetProperty("read").GetBoolean());
        Assert.False(access.GetProperty("write").GetBoolean());
        Assert.Contains("Saved 2 objects", output.ToString(), StringComparison.Ordinal);
        Assert.Contains("The meter reports 2 object_list entries.", output.ToString(), StringComparison.Ordinal);
        Assert.Contains("[1/2] 1.0.1.8.0.255", output.ToString(), StringComparison.Ordinal);
        Assert.Contains("[2/2] 0.0.96.1.0.255", output.ToString(), StringComparison.Ordinal);
        Assert.Contains(outputPath, output.ToString(), StringComparison.Ordinal);
        Assert.Contains("[0001] TX Aarq", output.ToString(), StringComparison.Ordinal);
        Assert.Contains("[0002] RX Aare", output.ToString(), StringComparison.Ordinal);
        Assert.Contains("GetRequest", output.ToString(), StringComparison.Ordinal);
        if (closeAfterData)
        {
            Assert.Contains("release failed", error.ToString(), StringComparison.Ordinal);
        }
        else
        {
            Assert.Equal("", error.ToString());
        }

        async Task ServeAsync()
        {
            using var tcp = await listener.AcceptTcpClientAsync(TestContext.Current.CancellationToken);
            var wrapper = Assert.IsType<WrapperFramingSettings>(settings.Channel.Framing);
            await using var channel = new FramedApduChannel<WrapperFrame>(new StreamByteTransport(tcp.GetStream(), leaveOpen: true),
                new WrapperCodec(), new WrapperApduAdapter(wrapper.ServerPort, wrapper.ClientPort));
            var codec = new ApduCodec();
            var aarq = await ReadAsync<AcseApdu>();
            Assert.Equal(AcseKind.Aarq, aarq.Kind);
            await SendAsync(AcseApdu.Aare(codec.EncodeBytes(new InitiateResponse(null, DlmsNegotiation.Version6,
                (uint)settings.Association.Conformance, 1024))));
            var get = await ReadAsync<LnRequest>();
            Assert.Equal(settings.Association.LogicalName, get.Operation.LogicalName.ToString());
            Assert.Equal(AssociationLnMembers.Attributes.ObjectList, get.Operation.Index);
            var list = DlmsValue.Array(Entry(3, 0, "1.0.1.8.0.255", 1), Entry(9000, 5, "0.0.96.1.0.255", 2));
            var bytes = new AxdrCodec().EncodeBytes(list);
            const int chunkLength = 13;
            uint blockNumber = 1;
            for (int offset = 0; offset < bytes.Length; offset += chunkLength, blockNumber++)
            {
                if (offset != 0)
                {
                    var next = await ReadAsync<GetNextRequest>();
                    Assert.Equal(blockNumber - 1, next.BlockNumber);
                    Assert.Equal(get.InvokeIdAndPriority, next.InvokeIdAndPriority);
                }

                int length = Math.Min(chunkLength, bytes.Length - offset);
                await SendAsync(new GetBlockResponse(get.InvokeIdAndPriority, offset + length == bytes.Length, blockNumber,
                    bytes.AsSpan(offset, length).ToArray().ToImmutableArray()));
            }

            if (!closeAfterData)
            {
                Assert.Equal(AcseKind.Rlrq, (await ReadAsync<AcseApdu>()).Kind);
                await SendAsync(AcseApdu.Release(response: true));
            }

            async Task<T> ReadAsync<T>() where T : DlmsApdu
            {
                var value = await channel.ReceiveAsync(TestContext.Current.CancellationToken).AsTask()
                    .WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
                Assert.True(value.HasValue);
                return Assert.IsType<T>(codec.Decode(new(value.Value)).Value);
            }

            ValueTask SendAsync(DlmsApdu value) => channel.SendAsync(codec.EncodeBytes(value), TestContext.Current.CancellationToken);
        }
    }
}
