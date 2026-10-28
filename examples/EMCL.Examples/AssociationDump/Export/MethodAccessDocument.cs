namespace EMCL.Examples.AssociationDump.Export;

internal sealed record MethodAccessDocument(byte RawMode, string Encoding, IReadOnlyList<string> Modes,
    bool? Invoke, bool? AuthenticationRequired, ProtectionDocument? Protection, byte UnknownBits);
