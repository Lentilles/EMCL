namespace EMCL.Examples.AssociationDump.Export;

internal sealed record ProtectionDocument(bool AuthenticatedRequest, bool EncryptedRequest, bool SignedRequest,
    bool AuthenticatedResponse, bool EncryptedResponse, bool SignedResponse);
