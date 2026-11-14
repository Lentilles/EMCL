using System.Collections.Immutable;

namespace EMCL.Protocol.Apdu;

/// <summary>Confirms processing of a DataNotification.</summary>
/// <param name="LongInvokeIdAndPriority">The long invoke identifier and flags from the notification.</param>
/// <param name="DateTime">An absent or twelve-octet COSEM timestamp.</param>
public sealed record DataNotificationConfirm(uint LongInvokeIdAndPriority, ImmutableArray<byte> DateTime) : DlmsApdu;
