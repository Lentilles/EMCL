namespace EMCL.Examples.AssociationDump.Export;

internal sealed record AssociationDocument(int SchemaVersion, DateTimeOffset ExportedAtUtc,
    AssociationSource Source, NegotiationDocument Negotiated, int ObjectCount, IReadOnlyList<ObjectDocument> Objects,
    IReadOnlyList<string> Warnings)
{
    internal const int CurrentSchemaVersion = 2;
}
