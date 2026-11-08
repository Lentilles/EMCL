using EMCL.Composition;
using EMCL.Core.Codecs;
using EMCL.Core.Objects;
using EMCL.Core.Operations;
using EMCL.Core.Values;
using EMCL.Transport.Channels;

namespace EMCL.Tests.Support;

internal static class TestData
{
    internal static byte[] FromHex(string text) => Convert.FromHexString(text.Replace(" ", ""));
    internal static DlmsValue Number(DlmsType type, long value) => DlmsValue.Number(type, value);
    internal static T DecodeComplete<T>(IIncrementalCodec<T> codec, byte[] wire)
    {
        for (int i = 0; i < wire.Length; i++)
        {
            Assert.Equal(DecodeStatus.NeedMoreData, codec.Decode(new(wire.AsMemory(0, i))).Status);
        }

        var result = codec.Decode(new(wire));
        Assert.Equal(DecodeStatus.Done, result.Status);
        Assert.True(result.Consumed > 0 && result.Consumed <= wire.Length);
        return result.Value!;
    }

    internal static DlmsValue Entry(ushort id, byte version, string ln, byte mode = 3) => DlmsValue.Structure(
        Number(DlmsType.UInt16, id),
        Number(DlmsType.UInt8, version),
        DlmsValue.Octets(LogicalName.Parse(ln).ToBytes()),
        DlmsValue.Structure(
            DlmsValue.Array(DlmsValue.Structure(Number(DlmsType.Int8, 2), Number(DlmsType.Enum, mode), DlmsValue.Null)),
            DlmsValue.Array(DlmsValue.Structure(Number(DlmsType.Int8, 1), Number(DlmsType.Enum, 1)))
        )
    );
    internal static DlmsValue ReadStoredValue(CosemObject o, sbyte index) => o.State.TryGet(index, out var v) ? v : throw new Exception($"Missing {index}");
    internal static ValueTask<OperationResult> ExecuteOperationAsync(EmclDevice d, CosemObject o, CosemOperationKind kind, sbyte index, DlmsValue? value = null, SelectiveAccess? selection = null) => d.Pipeline.ExecuteAsync(new(kind, o.Descriptor.Key.ClassId, o.LogicalName, index, value, selection), d.LocalContext, cancellationToken: TestContext.Current.CancellationToken);
    internal static async Task WriteValueAsync(EmclDevice d, CosemObject o, sbyte index, DlmsValue value) => Assert.Equal(CosemResultCode.Success, (await ExecuteOperationAsync(d, o, CosemOperationKind.Write, index, value)).Error);
    internal static DlmsValue CaptureDefinition(CosemObject o, sbyte index = 2, ushort dataIndex = 0) => DlmsValue.Structure(Number(DlmsType.UInt16, o.Descriptor.Key.ClassId), DlmsValue.Octets(o.LogicalName.ToBytes()), Number(DlmsType.Int8, index), Number(DlmsType.UInt16, dataIndex));
    internal static async Task AssertReceivedBytesAsync(IApduChannel channel, byte[] expected)
    {
        var payload = await channel.ReceiveAsync(cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(payload.HasValue);
        AssertBytesEqual(expected, payload.Value.Span);
    }

    internal static void AssertBytesEqual(ReadOnlySpan<byte> expected, ReadOnlySpan<byte> actual) => Assert.Equal(expected.ToArray(), actual.ToArray());
}
