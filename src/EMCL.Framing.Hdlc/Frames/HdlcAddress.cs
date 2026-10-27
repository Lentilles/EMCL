namespace EMCL.Framing.Hdlc.Frames;

/// <summary>Preserves an HDLC address value together with its one-, two-, or four-octet wire length.</summary>
/// <param name="Value">The concatenated seven-bit address groups, without extension-marker bits.</param>
/// <param name="Length">The address length in wire octets; one, two, or four.</param>
public readonly record struct HdlcAddress(uint Value, byte Length)
{
    /// <summary>Creates a one-octet client address from a seven-bit SAP.</summary>
    /// <param name="sap">The seven-bit client SAP.</param>
    public static HdlcAddress Client(byte sap) => sap <= HdlcAddressFormat.GroupMask ? new(sap, HdlcAddressFormat.ClientLength) : throw new ArgumentOutOfRangeException(nameof(sap));
    /// <summary>Creates a server address with seven-bit or extended fourteen-bit upper and lower components.</summary>
    /// <param name="upper">The upper server address component.</param>
    /// <param name="lower">The optional lower server address component.</param>
    /// <param name="extended">Whether to encode each server component using fourteen bits.</param>
    public static HdlcAddress Server(ushort upper, ushort? lower = null, bool extended = false)
    {
        if (!lower.HasValue)
        {
            if (upper > HdlcAddressFormat.GroupMask)
            {
                throw new ArgumentOutOfRangeException(nameof(upper));
            }

            return new(upper, HdlcAddressFormat.ClientLength);
        }

        if (extended)
        {
            if (upper > HdlcAddressFormat.ExtendedComponentMask || lower > HdlcAddressFormat.ExtendedComponentMask)
            {
                throw new ArgumentOutOfRangeException(nameof(upper));
            }

            return new(((uint)upper << HdlcAddressFormat.ExtendedComponentBits) | lower.Value, HdlcAddressFormat.ExtendedServerLength);
        }

        if (upper > HdlcAddressFormat.GroupMask || lower > HdlcAddressFormat.GroupMask)
        {
            throw new ArgumentOutOfRangeException(nameof(upper));
        }

        return new(((uint)upper << HdlcAddressFormat.GroupBits) | lower.Value, HdlcAddressFormat.ServerLength);
    }
}
