namespace EMCL.Objects.Definitions;

/// <summary>Identifies the extended members of SPODES Disconnect Control version 2.</summary>
public static class DisconnectControlMembers
{
    /// <summary>The delayed-mode attribute, wire index 129.</summary>
    public const sbyte DelayedMode = unchecked((sbyte)129);
    /// <summary>The delayed-disconnect initialization method, wire index 129.</summary>
    public const sbyte InitializeDelayed = unchecked((sbyte)129);
    /// <summary>The delayed-disconnect confirmation method, wire index 130.</summary>
    public const sbyte ConfirmDelayed = unchecked((sbyte)130);
    /// <summary>The temporary control block method, wire index 131.</summary>
    public const sbyte BlockControl = unchecked((sbyte)131);
}
