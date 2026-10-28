namespace EMCL.Examples.AssociationDump.Export;

internal sealed record ObjectDocument(string ObisCode, ushort ClassId, byte ClassVersion, string? ClassName,
    bool DefinitionKnown, IReadOnlyList<AttributeDocument> Attributes, IReadOnlyList<MethodDocument> Methods);
