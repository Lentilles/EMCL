using System.Collections.Immutable;
using EMCL.Core.Values;

namespace EMCL.Protocol.Apdu;

/// <summary>Represents an unsolicited DataNotification APDU with an optional timestamp and a DLMS Data body.</summary>
/// <param name="LongInvokeIdAndPriority">The original 32-bit long invoke identifier and priority field.</param>
/// <param name="DateTime">An empty timestamp or exactly twelve raw COSEM date-time octets.</param>
/// <param name="Body">The notification Data value.</param>
public sealed record DataNotification(uint LongInvokeIdAndPriority, ImmutableArray<byte> DateTime, DlmsValue Body) : DlmsApdu;
