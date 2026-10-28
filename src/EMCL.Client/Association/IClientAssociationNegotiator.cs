using EMCL.Protocol.Acse;

namespace EMCL.Client.Association;

/// <summary>Customizes ACSE fields and association-context validation without owning transport I/O.</summary>
/// <remarks>Implementations must not report authentication or protection that they have not verified. A negotiator can implement <see cref="IClientHighLevelAuthenticator"/> for an additional ACTION challenge exchange.</remarks>
public interface IClientAssociationNegotiator
{
    /// <summary>Creates the association request for the supplied proposal.</summary>
    /// <param name="proposal">The validated LN association proposal.</param>
    /// <returns>The AARQ to send.</returns>
    AcseApdu CreateRequest(LnAssociationOptions proposal);
    /// <summary>Validates an AARE and returns its accepted context, or throws on rejection.</summary>
    /// <param name="response">The received AARE.</param>
    /// <param name="proposal">The original proposal.</param>
    /// <returns>The validated negotiated context.</returns>
    NegotiatedAssociation AcceptResponse(AcseApdu response, LnAssociationOptions proposal);
    /// <summary>Creates a release request.</summary>
    /// <returns>The RLRQ to send.</returns>
    AcseApdu CreateReleaseRequest();
    /// <summary>Validates the peer release response or throws on failure.</summary>
    /// <param name="response">The received RLRE.</param>
    void AcceptReleaseResponse(AcseApdu response);
}
