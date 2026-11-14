using System.Collections.Immutable;
using EMCL.Core.Objects;
using EMCL.Core.Values;

namespace EMCL.Protocol.Apdu;

/// <summary>Pairs SPODES notification values with their capture descriptors.</summary>
public static class SpodesPushMessage
{
    /// <summary>Resolves the capture descriptors and checks the number of values in a Push message.</summary>
    /// <param name="notification">The decoded notification APDU.</param>
    /// <param name="configuredList">The agreed capture list used when bit 28 marks a message without an inline list.</param>
    /// <returns>The capture descriptors in the same order as the values, paired with their values.</returns>
    public static ImmutableArray<(DlmsValue Capture, DlmsValue Value)> Bind(DataNotification notification,
        DlmsValue? configuredList = null)
    {
        ArgumentNullException.ThrowIfNull(notification);
        if (notification.Body.Type != DlmsType.Structure)
        {
            throw new FormatException("The SPODES Push body must be a structure.");
        }

        var values = notification.Body.Items;
        bool configured = (notification.LongInvokeIdAndPriority & DataNotification.SelfDescriptive) != 0;
        var list = configured ? configuredList : values.FirstOrDefault();
        if (list is not { Type: DlmsType.Array } || list.Items.Length != values.Length)
        {
            throw new FormatException("The Push capture list and values have different lengths.");
        }

        if (!configured && (list.Items.IsEmpty || !IsCaptureListReference(list.Items[0])))
        {
            throw new FormatException("The first Push capture must reference the capture list itself.");
        }

        for (int i = 0; i < list.Items.Length; i++)
        {
            if (!IsCapture(list.Items[i]))
            {
                throw new FormatException($"The Push capture descriptor at position {i} is invalid.");
            }
        }

        return [.. list.Items.Zip(values)];
    }

    private static bool IsCaptureListReference(DlmsValue value)
        => IsCapture(value) && value.Items[0].AsUInt64() == CosemClassIds.PushSetup
            && value.Items[2].AsInt64() == 2 && value.Items[3].AsUInt64() == 0;

    private static bool IsCapture(DlmsValue value)
    {
        if (value.Type != DlmsType.Structure || value.Items.Length != 6)
        {
            return false;
        }

        var fields = value.Items;
        return fields[0].Type == DlmsType.UInt16 && fields[1].Type == DlmsType.OctetString
            && fields[1].Bytes.Length == LogicalName.EncodedLength && fields[2].Type == DlmsType.Int8
            && fields[3].Type == DlmsType.UInt16 && fields[4].Type == DlmsType.Structure
            && fields[4].Items.Length == 2 && fields[4].Items[0].Type == DlmsType.Enum
            && fields[5].Type == DlmsType.Array;
    }
}
