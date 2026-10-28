namespace EMCL.Examples.AssociationDump.Export;

internal sealed record MethodDocument(sbyte Index, string? Name, string? ParameterType, byte? ParameterTypeTag,
    bool DefinedByProfile, bool ReportedByAssociation, MethodAccessDocument? Access);
