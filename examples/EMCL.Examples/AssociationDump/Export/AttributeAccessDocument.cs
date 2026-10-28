namespace EMCL.Examples.AssociationDump.Export;

internal sealed record AttributeAccessDocument(byte RawMode, string Encoding, IReadOnlyList<string> Modes,
    bool? Read, bool? Write, bool? AuthenticationRequired, ProtectionDocument? Protection,
    byte UnknownBits, IReadOnlyList<int> Selectors);
