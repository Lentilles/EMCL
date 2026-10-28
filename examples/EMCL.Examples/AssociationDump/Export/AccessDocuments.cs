using EMCL.Core.Access;
using EMCL.Objects.Definitions;

namespace EMCL.Examples.AssociationDump.Export;

internal static class AccessDocuments
{
    private const string EnumerationV1 = "EnumerationV1";
    private const string BitmaskV3 = "BitmaskV3";
    private const string Unknown = "Unknown";
    private const AccessModeV3 ProtectionMask = AccessModeV3.AuthenticatedRequest | AccessModeV3.EncryptedRequest
        | AccessModeV3.SignedRequest | AccessModeV3.AuthenticatedResponse | AccessModeV3.EncryptedResponse | AccessModeV3.SignedResponse;
    private static readonly (AccessModeV3 Flag, string Name)[] ProtectionNames =
    [
        (AccessModeV3.AuthenticatedRequest, nameof(AccessModeV3.AuthenticatedRequest)),
        (AccessModeV3.EncryptedRequest, nameof(AccessModeV3.EncryptedRequest)),
        (AccessModeV3.SignedRequest, nameof(AccessModeV3.SignedRequest)),
        (AccessModeV3.AuthenticatedResponse, nameof(AccessModeV3.AuthenticatedResponse)),
        (AccessModeV3.EncryptedResponse, nameof(AccessModeV3.EncryptedResponse)),
        (AccessModeV3.SignedResponse, nameof(AccessModeV3.SignedResponse))
    ];

    internal static AttributeAccessDocument Attribute(AttributeAccess access, byte version)
    {
        var selectors = access.Selectors.Select(selector => (int)selector).ToArray();
        if (version == AssociationLnMembers.Version1)
        {
            var mode = (AttributeAccessModeV1)access.RawMode;
            bool known = Enum.IsDefined(mode);
            bool read = mode is AttributeAccessModeV1.ReadOnly or AttributeAccessModeV1.ReadWrite
                or AttributeAccessModeV1.AuthenticatedReadOnly or AttributeAccessModeV1.AuthenticatedReadWrite;
            bool write = mode is AttributeAccessModeV1.WriteOnly or AttributeAccessModeV1.ReadWrite
                or AttributeAccessModeV1.AuthenticatedWriteOnly or AttributeAccessModeV1.AuthenticatedReadWrite;
            bool authenticated = mode is AttributeAccessModeV1.AuthenticatedReadOnly or AttributeAccessModeV1.AuthenticatedWriteOnly
                or AttributeAccessModeV1.AuthenticatedReadWrite;
            return new(access.RawMode, EnumerationV1, [known ? mode.ToString() : Unknown], known ? read : null,
                known ? write : null, known ? authenticated : null, null, default, selectors);
        }

        RequireVersion3(version);
        var flags = (AccessModeV3)access.RawMode;
        var names = Modes(flags, method: false);
        return new(access.RawMode, BitmaskV3, names, Has(flags, AccessModeV3.Read), Has(flags, AccessModeV3.Write),
            null, Protection(flags), UnknownBits(flags, ProtectionMask | AccessModeV3.Read | AccessModeV3.Write), selectors);
    }

    internal static MethodAccessDocument Method(MethodAccess access, byte version)
    {
        if (version == AssociationLnMembers.Version1)
        {
            var mode = (MethodAccessModeV1)access.RawMode;
            bool known = Enum.IsDefined(mode);
            return new(access.RawMode, EnumerationV1, [known ? mode.ToString() : Unknown],
                known ? mode is MethodAccessModeV1.Access or MethodAccessModeV1.AuthenticatedAccess : null,
                known ? mode == MethodAccessModeV1.AuthenticatedAccess : null, null, default);
        }

        RequireVersion3(version);
        var flags = (AccessModeV3)access.RawMode;
        return new(access.RawMode, BitmaskV3, Modes(flags, method: true), Has(flags, AccessModeV3.Invoke), null,
            Protection(flags), UnknownBits(flags, ProtectionMask | AccessModeV3.Invoke));
    }

    private static IReadOnlyList<string> Modes(AccessModeV3 flags, bool method)
    {
        var names = new List<string>();
        if (Has(flags, AccessModeV3.Read))
        {
            names.Add(method ? nameof(AccessModeV3.Invoke) : nameof(AccessModeV3.Read));
        }

        if (!method && Has(flags, AccessModeV3.Write))
        {
            names.Add(nameof(AccessModeV3.Write));
        }

        names.AddRange(ProtectionNames.Where(pair => Has(flags, pair.Flag)).Select(pair => pair.Name));
        if (flags == AccessModeV3.None)
        {
            names.Add(nameof(AccessModeV3.None));
        }

        return names;
    }

    private static ProtectionDocument Protection(AccessModeV3 flags) => new(
        Has(flags, AccessModeV3.AuthenticatedRequest), Has(flags, AccessModeV3.EncryptedRequest), Has(flags, AccessModeV3.SignedRequest),
        Has(flags, AccessModeV3.AuthenticatedResponse), Has(flags, AccessModeV3.EncryptedResponse), Has(flags, AccessModeV3.SignedResponse));

    private static bool Has(AccessModeV3 flags, AccessModeV3 flag) => (flags & flag) != AccessModeV3.None;
    private static byte UnknownBits(AccessModeV3 flags, AccessModeV3 known) => (byte)(flags & ~known);

    private static void RequireVersion3(byte version)
    {
        if (version != AssociationLnMembers.Version3)
        {
            throw new NotSupportedException("Only Association LN v1 and v3 access encodings are supported.");
        }
    }
}
