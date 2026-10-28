namespace EMCL.Examples.AssociationDump.Export;

internal sealed record AttributeDocument(sbyte Index, string? Name, string? DlmsType, byte? DlmsTypeTag,
    bool DefinedByProfile, bool ReportedByAssociation, AttributeAccessDocument? Access);
