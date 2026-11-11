using EMCL.Client.Association;
using EMCL.Core.Codecs;
using EMCL.Protocol.Apdu;

namespace EMCL.Client.Sessions;

/// <summary>Configures bounded client exchanges and ownership of an already connected APDU channel.</summary>
public sealed record ClientSessionOptions
{
    /// <summary>Gets the association proposal.</summary>
    public LnAssociationOptions Association { get; init; } = new();
    /// <summary>Gets the timeout for an association, release, or complete operation including all GET or SET blocks; queue waiting uses caller cancellation.</summary>
    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(30);
    /// <summary>Optional delay before one retransmission of an unanswered AARQ on a channel that supports safe link-layer replay; null disables it.</summary>
    /// <remarks>A channel with its own acknowledgement timer handles AARQ through its link settings.</remarks>
    public TimeSpan? AssociationRetryDelay
    {
        get; init;
    }
    /// <summary>Gets the maximum concurrent operations, from one through the invoke-ID capacity; one is the default.</summary>
    public int MaxConcurrentRequests { get; init; } = 1;
    /// <summary>Gets the bounded number of notifications waiting for application consumption; overflow faults the session.</summary>
    public int NotificationCapacity { get; init; } = 64;
    /// <summary>Gets the priority for requests with automatically allocated invoke IDs.</summary>
    /// <remarks>High priority requires priority management to be proposed and negotiated.</remarks>
    public InvokePriority Priority { get; init; } = InvokePriority.Normal;
    /// <summary>Allows response Invoke-Id-And-Priority octets to differ from those sent by the client.</summary>
    /// <remarks>Applies to HLS ACTION and all subsequent operations, including block transfers.
    /// This compatibility option defaults to false and requires <see cref="MaxConcurrentRequests"/> to be one.</remarks>
    public bool IgnoreInvokeIdValidation
    {
        get; init;
    }
    /// <summary>Overrides the Invoke-Id-And-Priority octet for every request after association establishment.</summary>
    /// <remarks>Use only for peers that require a fixed octet. The value must encode a confirmed service with
    /// zero reserved bits and requires one request at a time. An explicit high-priority octet can be used
    /// without negotiating PriorityManagement. HLS ACTION is configured through the association proposal.</remarks>
    public byte? FixedInvokeIdAndPriority
    {
        get; init;
    }
    /// <summary>Gets the shared limits for APDUs, decoded values, accumulated GET blocks, and outgoing SET value bytes and block count.</summary>
    public ProtocolLimits Limits { get; init; } = new();
    /// <summary>Gets the time provider used for exchange deadlines.</summary>
    public TimeProvider TimeProvider { get; init; } = TimeProvider.System;
    /// <summary>Gets whether disposal leaves the channel open; false transfers channel ownership to the session.</summary>
    public bool LeaveOpen
    {
        get; init;
    }

    internal void Validate()
    {
        ArgumentNullException.ThrowIfNull(Association);
        ArgumentNullException.ThrowIfNull(Limits);
        ArgumentNullException.ThrowIfNull(TimeProvider);
        Association.Validate();
        Limits.Validate();
        if (RequestTimeout <= TimeSpan.Zero || RequestTimeout.TotalMilliseconds > uint.MaxValue - 1)
        {
            throw new ArgumentOutOfRangeException(nameof(RequestTimeout));
        }

        if (AssociationRetryDelay is { } retryDelay && (retryDelay <= TimeSpan.Zero || retryDelay >= RequestTimeout))
        {
            throw new ArgumentOutOfRangeException(nameof(AssociationRetryDelay));
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(MaxConcurrentRequests, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(MaxConcurrentRequests, InvokeIdEncoding.Capacity);
        LnAssociationOptions.ValidateInvokeIdAndPriority(FixedInvokeIdAndPriority, nameof(FixedInvokeIdAndPriority));
        if (FixedInvokeIdAndPriority.HasValue && MaxConcurrentRequests != 1)
        {
            throw new ArgumentException("A fixed invoke-id-and-priority requires one request at a time.", nameof(MaxConcurrentRequests));
        }

        if (IgnoreInvokeIdValidation && MaxConcurrentRequests != 1)
        {
            throw new ArgumentException("Ignoring invoke ID validation requires one request at a time.", nameof(MaxConcurrentRequests));
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(NotificationCapacity);
        if (NotificationCapacity > Limits.MaxCollectionItems)
        {
            throw new ArgumentOutOfRangeException(nameof(NotificationCapacity));
        }

        if (!Enum.IsDefined(Priority))
        {
            throw new ArgumentOutOfRangeException(nameof(Priority));
        }

        if (Priority == InvokePriority.High && (Association.Conformance & DlmsConformance.PriorityManagement) == DlmsConformance.None)
        {
            throw new ArgumentException("High priority requires a priority-management proposal.");
        }

        if (Association.MaxReceivePduSize > Limits.MaxMessageBytes)
        {
            throw new ArgumentException("The advertised client PDU size exceeds local resource limits.");
        }
    }
}
