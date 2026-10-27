namespace EMCL.Protocol.BlockTransfer;

/// <summary>Identifies an independently assembled GET transfer within a channel, logical device, association, and invoke identifier.</summary>
/// <param name="ChannelId">The identity of the underlying channel.</param>
/// <param name="LogicalDeviceId">The identity of the logical device executing the operation.</param>
/// <param name="AssociationId">The caller-defined identity of the association.</param>
/// <param name="InvokeId">The four-bit invoke identifier, without priority bits.</param>
public readonly record struct GetExchangeKey(string ChannelId, string LogicalDeviceId, string AssociationId, byte InvokeId);
