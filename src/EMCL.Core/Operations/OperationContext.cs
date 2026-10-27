using EMCL.Core.Access;

namespace EMCL.Core.Operations;

/// <summary>Carries the logical device, association permissions, and protection assurances established by the caller.</summary>
/// <remarks>Protection flags are assurances supplied by a trusted application layer. Parsing authentication fields does not establish these assurances.</remarks>
/// <param name="LogicalDeviceId">The identity of the logical device executing the operation.</param>
/// <param name="Association">An immutable permission snapshot, or null for unrestricted local operation.</param>
/// <param name="AuthenticatedRequest">Whether a trusted layer has authenticated the request.</param>
/// <param name="EncryptedRequest">Whether a trusted layer has verified request encryption.</param>
/// <param name="SignedRequest">Whether a trusted layer has verified the request signature.</param>
/// <param name="AuthenticatedResponse">Whether the response will be authenticated by a trusted layer.</param>
/// <param name="EncryptedResponse">Whether the response will be encrypted by a trusted layer.</param>
/// <param name="SignedResponse">Whether the response will be signed by a trusted layer.</param>
public sealed record OperationContext(string LogicalDeviceId, AssociationAccessContext? Association = null, bool AuthenticatedRequest = false, bool EncryptedRequest = false, bool SignedRequest = false, bool AuthenticatedResponse = false, bool EncryptedResponse = false, bool SignedResponse = false);
