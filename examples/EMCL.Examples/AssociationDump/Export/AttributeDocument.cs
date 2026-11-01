namespace EMCL.Examples.AssociationDump.Export;

internal sealed record AttributeDocument(sbyte Index, string? Name, string? DlmsType, byte? DlmsTypeTag,
    bool DefinedByLibrary, bool ReportedByAssociation, AttributeAccessDocument? Access);
