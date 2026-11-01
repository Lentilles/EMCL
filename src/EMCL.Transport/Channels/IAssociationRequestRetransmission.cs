namespace EMCL.Transport.Channels;

/// <summary>Allows one HDLC link-layer retransmission of an unanswered association request.</summary>
/// <remarks>The implementation must replay the original information frame and sequence number. Acknowledged or answered requests must not be retransmitted.</remarks>
public interface IAssociationRequestRetransmission
{
    /// <summary>Retransmits the first information frame if it is still unacknowledged and unanswered.</summary>
    /// <returns>True if the original frame was replayed; false if the link has advanced.</returns>
    ValueTask<bool> RetryUnansweredAssociationRequestAsync(CancellationToken cancellationToken = default);
}
