using EMCL.Composition;
using EMCL.Core.Objects;
using EMCL.Core.Operations;
using EMCL.Core.Values;

namespace EMCL.Tests.Objects.ProfileGeneric;

public sealed class ProfileSortingTests
{
    [Theory]
    [InlineData(1, 1L, 2L)]
    [InlineData(2, 2L, 1L)]
    [InlineData(3, 2L, 1L)]
    [InlineData(4, -3L, 1L)]
    [InlineData(5, 1L, 2L)]
    [InlineData(6, -3L, 2L)]
    public async Task RetainsRowsAccordingToTheSelectedSortMethod(int sortMethod, long first, long second)
    {
        var device = new EmclBuilder().Build("meter");
        var register = device.Add(3, 0, LogicalName.Parse("1.0.1.8.0.255"));
        var profile = device.Add(7, 1, LogicalName.Parse("1.0.99.1.0.255"));
        await WriteValueAsync(device, profile, 3, DlmsValue.Array(CaptureDefinition(register)));
        await WriteValueAsync(device, profile, 6, CaptureDefinition(register));
        await WriteValueAsync(device, profile, 5, Number(DlmsType.Enum, sortMethod));
        await WriteValueAsync(device, profile, 8, Number(DlmsType.UInt32, 2));
        foreach (int value in new[]
        {
            -3,
            1,
            2
        }

        )
        {
            register.State.Set(2, Number(DlmsType.Int32, value));
            var result = await ExecuteOperationAsync(device, profile, CosemOperationKind.Invoke, 2, Number(DlmsType.Int8, 0));
            Assert.Equal(CosemResultCode.Success, result.Error);
        }

        long[] actual = ReadStoredValue(profile, 2).Items.Select(row => row.Items[0].AsInt64()).ToArray();
        Assert.Equal(new[] { first, second }, actual);
    }

    [Fact]
    public async Task ChangingCaptureDefinitionsClearsDependentBuffers()
    {
        var device = new EmclBuilder().Build("meter");
        var register = device.Add(3, 0, LogicalName.Parse("1.0.1.8.0.255"));
        register.State.Set(2, Number(DlmsType.Int32, 42));
        var inner = device.Add(7, 1, LogicalName.Parse("1.0.99.1.0.255"));
        await WriteValueAsync(device, inner, 3, DlmsValue.Array(CaptureDefinition(register)));
        Assert.Equal(CosemResultCode.Success, (await ExecuteOperationAsync(device, inner, CosemOperationKind.Invoke, 2, Number(DlmsType.Int8, 0))).Error);
        var outer = device.Add(7, 1, LogicalName.Parse("1.0.99.2.0.255"));
        await WriteValueAsync(device, outer, 3, DlmsValue.Array(CaptureDefinition(inner)));
        Assert.Equal(CosemResultCode.Success, (await ExecuteOperationAsync(device, outer, CosemOperationKind.Invoke, 2, Number(DlmsType.Int8, 0))).Error);
        Assert.Single(ReadStoredValue(outer, 2).Items);
        await WriteValueAsync(device, inner, 3, DlmsValue.Array(CaptureDefinition(register)));
        Assert.Empty(ReadStoredValue(outer, 2).Items);
        Assert.Equal(0L, ReadStoredValue(outer, 7).AsInt64());
    }
}
