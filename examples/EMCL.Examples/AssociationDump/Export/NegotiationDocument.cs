namespace EMCL.Examples.AssociationDump.Export;

internal sealed record NegotiationDocument(byte DlmsVersion, IReadOnlyList<string> Conformance,
    ushort ClientMaxReceivePduSize, ushort ServerMaxReceivePduSize);
