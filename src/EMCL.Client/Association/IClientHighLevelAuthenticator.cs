using EMCL.Core.Operations;
using EMCL.Core.Values;
using EMCL.Protocol.Acse;

namespace EMCL.Client.Association;

/// <summary>Completes a high-level association with one COSEM ACTION challenge exchange.</summary>
/// <remarks>Implementations may provide a different mechanism while retaining the standard client session and APDU channel.</remarks>
public interface IClientHighLevelAuthenticator
{
    /// <summary>Builds the authentication ACTION after the AARE has been validated.</summary>
    /// <param name="request">The AARQ sent by the client.</param>
    /// <param name="response">The AARE received from the server.</param>
    /// <param name="proposal">The original association settings.</param>
    /// <returns>The challenge-response operation, or null when no additional exchange is needed.</returns>
    CosemOperation? CreateAuthenticationOperation(AcseApdu request, AcseApdu response, LnAssociationOptions proposal);

    /// <summary>Validates the server's ACTION return value; throws if authentication failed.</summary>
    /// <param name="value">The ACTION return value, or null if none was returned.</param>
    /// <param name="request">The AARQ sent by the client.</param>
    /// <param name="response">The AARE received from the server.</param>
    /// <param name="proposal">The original association settings.</param>
    void ValidateAuthenticationResponse(DlmsValue? value, AcseApdu request, AcseApdu response, LnAssociationOptions proposal);
}
