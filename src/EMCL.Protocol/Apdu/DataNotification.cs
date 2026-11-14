using System.Collections.Immutable;
using EMCL.Core.Values;

namespace EMCL.Protocol.Apdu;

/// <summary>Represents an unsolicited DataNotification APDU with an optional timestamp and a DLMS Data body.</summary>
/// <param name="LongInvokeIdAndPriority">The original 32-bit long invoke identifier and priority field.</param>
/// <param name="DateTime">An empty timestamp or exactly twelve raw COSEM date-time octets.</param>
/// <param name="Body">The notification Data value.</param>
public sealed record DataNotification(uint LongInvokeIdAndPriority, ImmutableArray<byte> DateTime, DlmsValue Body) : DlmsApdu
{
    /// <summary>The long invoke bit requesting application confirmation.</summary>
    public const uint ConfirmedService = 1U << 30;
    /// <summary>The long invoke bit indicating the self-descriptive form.</summary>
    public const uint SelfDescriptive = 1U << 28;
    /// <summary>Gets whether an application confirmation is expected.</summary>
    public bool IsConfirmed => (LongInvokeIdAndPriority & ConfirmedService) != 0;
}
