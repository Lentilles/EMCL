using EMCL.Core.Descriptors;
using EMCL.Core.Objects;
using EMCL.Core.Values;

using static EMCL.Objects.Definitions.SpodesSchema;

namespace EMCL.Objects.Definitions;

internal static class CommunicationDefinitions
{
    private static readonly ValueShape LcpOptions = Array(OneOf(
        PppOption(1, DlmsType.UInt16),
        PppOption(2, DlmsType.UInt32),
        PppOption(3, DlmsType.UInt16),
        PppOption(5, DlmsType.UInt32),
        PppOption(7, DlmsType.Boolean),
        PppOption(8, DlmsType.Boolean),
        PppOption(9, DlmsType.UInt8),
        PppOption(13, Structure(DlmsType.Boolean, DlmsType.UInt8, DlmsType.UInt8, DlmsType.OctetString))));

    private static readonly ValueShape IpcpOptions = Array(OneOf(
        PppOption(2, DlmsType.UInt16),
        PppOption(3, DlmsType.UInt32),
        PppOption(20, Array(DlmsType.UInt32)),
        PppOption(21, DlmsType.Boolean),
        PppOption(22, DlmsType.Boolean)));

    private static readonly ValueShape PppAuthentication = OneOf(
        Of(DlmsType.Null),
        Structure(DlmsType.OctetString, DlmsType.OctetString), // PAP
        Structure(DlmsType.OctetString, DlmsType.UInt8), // CHAP
        Structure(DlmsType.Boolean, DlmsType.Boolean, DlmsType.Boolean)); // EAP

    internal static CosemClassDescriptor PppSetup() => Class(CosemClassIds.PppSetup, 0, "PPP Setup",
        [
            Attr(2, "PHY_reference", Ln),
            Attr(3, "LCP_options", LcpOptions),
            Attr(4, "IPCP_options", IpcpOptions),
            Attr(5, "PPP_authentication", PppAuthentication)
        ]);

    internal static CosemClassDescriptor PortProtection() =>
        Class(CosemClassIds.CommunicationPortProtection, 0, "Communication Port Protection",
        [
            Attr(2, "protection_mode", Range(DlmsType.Enum, 0, 2)),
            Attr(3, "allowed_failed_attempts", DlmsType.UInt16),
            Attr(4, "initial_lockout_time", DlmsType.UInt32),
            Attr(5, "steepness_factor", DlmsType.UInt8),
            Attr(6, "max_lockout_time", DlmsType.UInt32),
            Attr(7, "port_reference", Octets(6, allowEmpty: true)),
            Attr(8, "protection_status", Range(DlmsType.Enum, 0, 2)),
            Attr(9, "failed_attempts", DlmsType.UInt32),
            Attr(10, "cumulative_failed_attempts", DlmsType.UInt32)
        ], Command(1, "reset"));

    private static ValueShape PppOption(byte type, ValueShape data) =>
        Structure(Range(DlmsType.UInt8, type, type), DlmsType.UInt8, data);
}
