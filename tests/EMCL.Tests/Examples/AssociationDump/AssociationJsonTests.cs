using System.Text.Json;
using EMCL.Core.Access;
using EMCL.Examples.AssociationDump.Export;
using EMCL.Objects.Definitions;

namespace EMCL.Tests.Examples.AssociationDump;

public sealed class AssociationJsonTests
{
    [Fact]
    public async Task ExportsObisMembersPermissionsSelectorsAndUnknownDefinitionsWithoutSecrets()
    {
        using var directory = new ExportTestDirectory();
        string path = Path.Combine(directory.Path, "association.json");
        var settings = ExportTestData.Configuration(path);
        settings = settings with
        {
            Association = settings.Association with
            {
                Authentication = EMCL.Client.Association.ClientAuthentication.Low,
                Password = "secret-not-for-export"
            }
        };
        var document = ExportTestData.Document(settings, (byte)(AccessModeV3.Read | AccessModeV3.EncryptedRequest | AccessModeV3.AuthenticatedResponse),
            (byte)(AccessModeV3.Invoke | AccessModeV3.Write | AccessModeV3.SignedRequest));
        await AssociationJsonFile.WriteAsync(path, document, overwrite: false, TestContext.Current.CancellationToken);
        string json = await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken);
        Assert.DoesNotContain("secret-not-for-export", json, StringComparison.Ordinal);
        Assert.DoesNotContain("password", json, StringComparison.OrdinalIgnoreCase);
        using var parsed = JsonDocument.Parse(json);
        var root = parsed.RootElement;
        Assert.Equal(3, root.GetProperty("schemaVersion").GetInt32());
        Assert.False(root.GetProperty("source").TryGetProperty("profileId", out _));
        Assert.Equal(2, root.GetProperty("objectCount").GetInt32());
        var objects = root.GetProperty("objects").EnumerateArray().ToArray();
        Assert.Equal("0.0.96.1.0.255", objects[0].GetProperty("obisCode").GetString());
        Assert.False(objects[0].GetProperty("definitionKnown").GetBoolean());
        Assert.Equal(JsonValueKind.Null, objects[0].GetProperty("className").ValueKind);
        var unknownAttribute = Assert.Single(objects[0].GetProperty("attributes").EnumerateArray());
        Assert.Equal(-2, unknownAttribute.GetProperty("index").GetInt32());
        Assert.Equal(JsonValueKind.Null, unknownAttribute.GetProperty("name").ValueKind);
        Assert.Equal(JsonValueKind.Null, unknownAttribute.GetProperty("dlmsType").ValueKind);
        var register = objects[1];
        Assert.Equal("1.0.1.8.0.255", register.GetProperty("obisCode").GetString());
        Assert.Equal(3, register.GetProperty("classId").GetInt32());
        var attributes = register.GetProperty("attributes").EnumerateArray().ToArray();
        var logicalName = attributes.Single(attribute => attribute.GetProperty("index").GetInt32() == 1);
        Assert.True(logicalName.GetProperty("definedByLibrary").GetBoolean());
        Assert.False(logicalName.GetProperty("reportedByAssociation").GetBoolean());
        Assert.Equal(JsonValueKind.Null, logicalName.GetProperty("access").ValueKind);
        var value = attributes.Single(attribute => attribute.GetProperty("index").GetInt32() == 2);
        Assert.Equal("value", value.GetProperty("name").GetString());
        var access = value.GetProperty("access");
        Assert.Equal(41, access.GetProperty("rawMode").GetInt32());
        Assert.True(access.GetProperty("read").GetBoolean());
        Assert.False(access.GetProperty("write").GetBoolean());
        Assert.True(access.GetProperty("protection").GetProperty("encryptedRequest").GetBoolean());
        Assert.True(access.GetProperty("protection").GetProperty("authenticatedResponse").GetBoolean());
        Assert.Equal(new[] { 1, 2 }, access.GetProperty("selectors").EnumerateArray().Select(selector => selector.GetInt32()));
        Assert.DoesNotContain("Invoke", access.GetProperty("modes").EnumerateArray().Select(mode => mode.GetString()));
        var additional = attributes.Single(attribute => attribute.GetProperty("index").GetInt32() == 7);
        Assert.False(additional.GetProperty("definedByLibrary").GetBoolean());
        Assert.False(additional.GetProperty("access").GetProperty("read").GetBoolean());
        var reset = register.GetProperty("methods").EnumerateArray().Single(method => method.GetProperty("index").GetInt32() == 1);
        Assert.Equal("reset", reset.GetProperty("name").GetString());
        Assert.True(reset.GetProperty("access").GetProperty("invoke").GetBoolean());
        Assert.Equal(2, reset.GetProperty("access").GetProperty("unknownBits").GetInt32());
        Assert.DoesNotContain("Read", reset.GetProperty("access").GetProperty("modes").EnumerateArray().Select(mode => mode.GetString()));
    }

    [Theory]
    [InlineData(1, true, true)]
    [InlineData(3, false, false)]
    public void InterpretsTheSameAccessOctetsAccordingToAssociationVersion(byte version, bool readable, bool invokable)
    {
        var settings = ExportTestData.Configuration("unused.json");
        settings = settings with
        {
            Association = settings.Association with
            {
                Version = version
            }
        };
        var document = ExportTestData.Document(settings, attributeMode: 4, methodMode: 2);
        var register = document.Objects.Single(obj => obj.ClassId == 3);
        var access = register.Attributes.Single(attribute => attribute.Index == 2).Access!;
        var method = register.Methods.Single(item => item.Index == 1).Access!;
        Assert.Equal(readable, access.Read);
        Assert.Equal(invokable, method.Invoke);
        Assert.Equal((byte)4, access.RawMode);
        Assert.Equal((byte)2, method.RawMode);
        if (version == AssociationLnMembers.Version1)
        {
            Assert.Equal("EnumerationV1", access.Encoding);
            Assert.True(access.AuthenticationRequired);
            Assert.True(method.AuthenticationRequired);
            Assert.Null(access.Protection);
        }
        else
        {
            Assert.Equal("BitmaskV3", access.Encoding);
            Assert.Null(access.AuthenticationRequired);
            Assert.True(access.Protection!.AuthenticatedRequest);
            Assert.Equal((byte)2, method.UnknownBits);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedOrCanceledSavePreservesExistingFileAndRemovesTemporaryOutput(bool cancel)
    {
        using var directory = new ExportTestDirectory();
        string path = Path.Combine(directory.Path, "association.json");
        await File.WriteAllTextAsync(path, "original", TestContext.Current.CancellationToken);
        var document = ExportTestData.Document(ExportTestData.Configuration(path), 1, 1);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        if (cancel)
        {
            await cancellation.CancelAsync();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
                await AssociationJsonFile.WriteAsync(path, document, overwrite: true, cancellation.Token));
        }
        else
        {
            await Assert.ThrowsAsync<IOException>(async () =>
                await AssociationJsonFile.WriteAsync(path, document, overwrite: false, TestContext.Current.CancellationToken));
        }

        Assert.Equal("original", await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
        Assert.Single(Directory.GetFiles(directory.Path));
    }
}
