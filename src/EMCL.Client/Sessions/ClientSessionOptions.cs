using EMCL.Client.Association;
using EMCL.Core.Codecs;
using EMCL.Protocol.Apdu;

namespace EMCL.Client.Sessions;

/// <summary>Configures bounded client exchanges and ownership of an already connected APDU channel.</summary>
public sealed record ClientSessionOptions
{
    /// <summary>Gets the association proposal.</summary>
    public LnAssociationOptions Association { get; init; } = new();
    /// <summary>Gets the timeout for an association, release, or complete operation including all GET blocks; queue waiting uses caller cancellation.</summary>
    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(30);
    /// <summary>Optional delay before one retransmission of an unanswered AARQ on a channel that supports safe link-layer replay; null disables it.</summary>
    public TimeSpan? AssociationRetryDelay { get; init; }
    /// <summary>Gets the maximum concurrent operations, from one through the invoke-ID capacity; one is the default.</summary>
    public int MaxConcurrentRequests { get; init; } = 1;
    /// <summary>Gets the bounded number of notifications waiting for application consumption; overflow faults the session.</summary>
    public int NotificationCapacity { get; init; } = 64;
    /// <summary>Gets whether requests use high priority; priority management must be proposed and negotiated.</summary>
    public bool HighPriority
    {
        get; init;
    }
    /// <summary>Gets the shared limits for APDUs, decoded values, and accumulated GET blocks.</summary>
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
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(NotificationCapacity);
        if (NotificationCapacity > Limits.MaxCollectionItems)
        {
            throw new ArgumentOutOfRangeException(nameof(NotificationCapacity));
        }

        if (HighPriority && (Association.Conformance & DlmsConformance.PriorityManagement) == DlmsConformance.None)
        {
            throw new ArgumentException("High priority requires a priority-management proposal.");
        }

        if (Association.MaxReceivePduSize > Limits.MaxMessageBytes)
        {
            throw new ArgumentException("The advertised client PDU size exceeds local resource limits.");
        }
    }
}
