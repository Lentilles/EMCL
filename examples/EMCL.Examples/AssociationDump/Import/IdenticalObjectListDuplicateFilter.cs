using EMCL.Core.Codecs;
using EMCL.Core.Objects;
using EMCL.Core.Values;
using EMCL.Protocol.Axdr;

namespace EMCL.Examples.AssociationDump.Import;

internal static class IdenticalObjectListDuplicateFilter
{
    private const int ObjectEntryFieldCount = 4;
    private const int LogicalNameFieldIndex = 2;

    internal static (DlmsValue Value, string[] Warnings) Apply(DlmsValue value, ProtocolLimits limits)
    {
        if (value.Type != DlmsType.Array)
        {
            return (value, []);
        }

        var codec = new AxdrCodec(limits);
        var seen = new Dictionary<LogicalName, byte[]>();
        var unique = new List<DlmsValue>();
        var warnings = new List<string>();
        foreach (var entry in value.Items)
        {
            if (entry.Type != DlmsType.Structure || entry.Items.Length != ObjectEntryFieldCount
                || entry.Items[LogicalNameFieldIndex].Type != DlmsType.OctetString
                || entry.Items[LogicalNameFieldIndex].Bytes.Length != LogicalName.EncodedLength)
            {
                unique.Add(entry);
                continue;
            }

            var name = LogicalName.FromBytes(entry.Items[LogicalNameFieldIndex].Bytes.AsSpan());
            byte[] encoded = codec.EncodeBytes(entry);
            if (seen.TryGetValue(name, out var earlier) && earlier.AsSpan().SequenceEqual(encoded))
            {
                warnings.Add($"Skipped byte-identical duplicate object_list entry {name}.");
                continue;
            }

            seen.TryAdd(name, encoded);
            unique.Add(entry);
        }

        return (DlmsValue.Array([.. unique]), [.. warnings]);
    }
}
