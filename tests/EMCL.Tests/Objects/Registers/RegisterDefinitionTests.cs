using EMCL.Composition;
using EMCL.Core.Codecs;
using EMCL.Core.Objects;
using EMCL.Core.Operations;
using EMCL.Core.Values;
using EMCL.Protocol.Axdr;

namespace EMCL.Tests.Objects.Registers;

public sealed class RegisterDefinitionTests
{
    [Theory]
    [InlineData(3, DlmsType.OctetString)]
    [InlineData(1, DlmsType.DateTime)]
    public void ImportsKnownNewClassesAndPreservesUnknownVersions(byte associationVersion, DlmsType timestampType)
    {
        var builder = new EmclBuilder();
        builder.Definitions.Register(ExtendedRegisterClassDefinition.Create(timestampType));
        builder.Definitions.Register(DemandRegisterClassDefinition.Create(timestampType));
        var list = DlmsValue.Array(Entry(4, 0, "1.0.1.8.0.255"), Entry(5, 0, "1.0.1.6.0.255"), Entry(6, 0, "0.0.14.0.0.255"));
        var bytes = new AxdrCodec().EncodeBytes(list);
        var imported = builder.CreateImporter().Import(new ReadOnlySequence<byte>(bytes), "meter", "public", associationVersion);
        Assert.All(imported.Catalog.Objects, register => Assert.True(register.Descriptor.IsKnown));
        Assert.Equal(timestampType, imported.Catalog.Find(LogicalName.Parse("1.0.1.8.0.255"))!.Descriptor.Attributes.Single(a => a.Index == 5).Type);
        Assert.Equal(timestampType, imported.Catalog.Find(LogicalName.Parse("1.0.1.6.0.255"))!.Descriptor.Attributes.Single(a => a.Index == 6).Type);
        var future = builder.CreateImporter().Import(DlmsValue.Array(Entry(4, 1, "1.0.1.8.0.255")),
            "future", "public", associationVersion);
        Assert.False(Assert.Single(future.Catalog.Objects).Descriptor.IsKnown);
    }

    [Theory]
    [InlineData(4, 3, 4, 5)]
    [InlineData(5, 4, 5, 6)]
    public async Task ValidatesScalerStatusAndTimestampWireShape(ushort classId, sbyte scalerIndex, sbyte statusIndex, sbyte timeIndex)
    {
        foreach (var timeType in new[] { DlmsType.OctetString, DlmsType.DateTime })
        {
            var builder = new EmclBuilder();
            builder.Definitions.Register(ExtendedRegisterClassDefinition.Create(timeType));
            builder.Definitions.Register(DemandRegisterClassDefinition.Create(timeType));
            var device = builder.Build("meter");
            var register = device.Add(classId, 0, LogicalName.Parse("1.0.1.8.0.255"));
            Assert.Equal(CosemError.TypeUnmatched, (await Write(scalerIndex,
                DlmsValue.Structure(Number(DlmsType.UInt8, 0), Number(DlmsType.Enum, 30)))).Error);
            Assert.Equal(CosemError.Success, (await Write(scalerIndex,
                DlmsValue.Structure(Number(DlmsType.Int8, -3), Number(DlmsType.Enum, 30)))).Error);
            Assert.Equal(CosemError.TypeUnmatched, (await Write(statusIndex, Number(DlmsType.Int32, 0))).Error);
            Assert.Equal(CosemError.Success, (await Write(statusIndex, DlmsValue.Null)).Error);
            Assert.Equal(CosemError.TypeUnmatched, (await Write(timeIndex, new(timeType, new byte[4]))).Error);
            Assert.Equal(CosemError.Success, (await Write(timeIndex, new(timeType, new byte[12]))).Error);

            ValueTask<OperationResult> Write(sbyte index, DlmsValue value) => ExecuteOperationAsync(device, register,
                CosemOperationKind.Write, index, value);
        }
    }
}
