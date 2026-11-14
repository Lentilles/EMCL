using EMCL.Core.Descriptors;
using EMCL.Core.Objects;
using EMCL.Core.Values;
using static EMCL.Objects.Definitions.SpodesSchema;

namespace EMCL.Objects.Definitions;

internal static partial class SpodesDefinitions
{
    private static readonly ValueShape Qos = Structure(DlmsType.UInt8, DlmsType.UInt8, DlmsType.UInt8, DlmsType.UInt8, DlmsType.UInt8);
    private static readonly ValueShape Ipv6Address = Octets(16);
    private static readonly ValueShape Ipv6AddressParameter = Structure(Range(DlmsType.Enum, 0, 2), Ipv6Address);

    private static CosemClassDescriptor[] Version5Network() =>
    [
        Class(CosemClassIds.AutoConnect, 2, "Auto Connect",
            [Attr(2, "mode", DlmsType.Enum), Attr(3, "repetitions", DlmsType.UInt8), Attr(4, "repetition_delay", DlmsType.UInt16),
             Attr(5, "calling_window", Array(Structure(DateTimeOctets, DateTimeOctets))), Attr(6, "destination_list", Array(DlmsType.OctetString))],
            Command(1, "connect")),
        Class(CosemClassIds.Ipv4Setup, 0, "IPv4 Setup",
            [Attr(2, "DL_reference", Ln), Attr(3, "IP_address", DlmsType.UInt32), Attr(4, "multicast_IP_address", Array(DlmsType.UInt32)),
             Attr(5, "IP_options", Array(Structure(DlmsType.UInt8, DlmsType.UInt8, DlmsType.OctetString))), Attr(6, "subnet_mask", DlmsType.UInt32),
             Attr(7, "gateway_IP_address", DlmsType.UInt32), Attr(8, "use_DHCP_flag", DlmsType.Boolean),
             Attr(9, "primary_DNS_address", DlmsType.UInt32), Attr(10, "secondary_DNS_address", DlmsType.UInt32)],
            Method(1, "add_mc_IP_address", DlmsType.UInt32), Method(2, "delete_mc_IP_address", DlmsType.UInt32),
            Method(3, "get_nbof_mc_IP_addresses", Zero, Of(DlmsType.UInt8))),
        Class(CosemClassIds.MacAddressSetup, 0, "MAC Address Setup", [Attr(2, "MAC_address", Octets(6))]),
        Class(CosemClassIds.GprsModemSetup, 0, "GPRS Modem Setup",
            [Attr(2, "APN", DlmsType.OctetString), Attr(3, "PIN_code", DlmsType.UInt16), Attr(4, "quality_of_service", Structure(Qos, Qos))]),
        Class(CosemClassIds.GsmDiagnostic, 2, "GSM Diagnostic",
            [Attr(2, "operator", DlmsType.VisibleString), Attr(3, "status", Range(DlmsType.Enum, 0, 5)),
             Attr(4, "cs_attachment", Range(DlmsType.Enum, 0, 2)), Attr(5, "ps_status", Range(DlmsType.Enum, 0, 9)),
             Attr(6, "cell_info", Structure(DlmsType.UInt32, DlmsType.UInt16, DlmsType.UInt8, DlmsType.UInt8, DlmsType.UInt16, DlmsType.UInt16, DlmsType.UInt32)),
             Attr(7, "adjacent_cells", Array(Structure(DlmsType.UInt32, DlmsType.UInt8))), Attr(8, "capture_time", DlmsType.DateTime)]),
        Class(CosemClassIds.Ipv6Setup, 0, "IPv6 Setup",
            [Attr(2, "DL_reference", Ln), Attr(3, "address_config_mode", Range(DlmsType.Enum, 0, 3)),
             Attr(4, "unicast_IPv6_addresses", Array(Ipv6Address)), Attr(5, "multicast_IPv6_addresses", Array(Ipv6Address)),
             Attr(6, "gateway_IPv6_addresses", Array(Ipv6Address)), Attr(7, "primary_DNS_address", Octets(16, true)),
             Attr(8, "secondary_DNS_address", Octets(16, true)), Attr(9, "traffic_class", Range(DlmsType.UInt8, 0, 63)),
             Attr(10, "neighbor_discovery_setup", Array(Structure(Range(DlmsType.UInt8, 1, 255), DlmsType.UInt16, DlmsType.UInt32)))],
            Method(1, "add_IPv6_address", Ipv6AddressParameter), Method(2, "remove_IPv6_address", Ipv6AddressParameter)),
        Class(CosemClassIds.LteMonitoring, 1, "LTE Monitoring",
            [Attr(2, "LTE_network_parameters", Structure(DlmsType.UInt16, DlmsType.UInt16, DlmsType.UInt32, DlmsType.UInt16, DlmsType.UInt32, DlmsType.UInt16, DlmsType.Int8, DlmsType.Int8, DlmsType.Int8)),
             Attr(3, "LTE_quality_of_service", Structure(DlmsType.Int8, DlmsType.Int8, DlmsType.Int8, DlmsType.Enum))])
    ];

    private static CosemClassDescriptor[] Radios()
    {
        var zigbeeAddress = Octets(8);
        var zigbeeKey = Octets(16);
        var zigbeeBackupEntries = Array(Structure(zigbeeAddress, zigbeeKey));
        return
        [
            Class(CosemClassIds.ZigBeeStartup, 0, "ZigBee SAS Startup",
                [Attr(2, "short_address", DlmsType.UInt16), Attr(3, "extended_pan_id", Octets(8, true)), Attr(4, "pan_id", DlmsType.UInt16),
                 Attr(5, "channel_mask", DlmsType.UInt32), Attr(6, "protocol_version", DlmsType.UInt8), Attr(7, "stack_profile", DlmsType.Enum),
                 Attr(8, "start_up_control", DlmsType.UInt8), Attr(9, "trust_center_address", Octets(8, true)),
                 Attr(10, "link_key", Octets(16, true)), Attr(11, "network_key", Octets(16, true)), Attr(12, "use_insecure_join", DlmsType.Boolean)]),
            Class(CosemClassIds.ZigBeeJoin, 0, "ZigBee SAS Join",
                [Attr(2, "scan_attempts", DlmsType.UInt8), Attr(3, "time_between_scans", DlmsType.UInt16), Attr(4, "rejoin_interval", DlmsType.UInt16),
                 Attr(5, "rejoin_retry_interval", DlmsType.UInt16)]),
            Class(CosemClassIds.ZigBeeFragmentation, 0, "ZigBee SAS APS Fragmentation",
                [Attr(2, "aps_interframe_delay", DlmsType.UInt16), Attr(3, "aps_max_window_size", DlmsType.UInt16)]),
            Class(CosemClassIds.ZigBeeNetworkControl, 0, "ZigBee Network Control",
                [Attr(2, "enable_disable_joining", DlmsType.Boolean), Attr(3, "join_timeout", DlmsType.UInt16),
                 Attr(4, "active_devices", Array(Structure(zigbeeAddress, DlmsType.BitString, DlmsType.Int8, DlmsType.Int8, DlmsType.Int8, DlmsType.UInt8, DlmsType.UInt8, DlmsType.UInt8,
                     DateTimeOctets, DlmsType.UInt8, DlmsType.UInt8, DlmsType.UInt8, DlmsType.UInt8, DlmsType.UInt8)))],
                Method(1, "register_device", Structure(zigbeeAddress, DlmsType.Enum, zigbeeKey, DlmsType.Enum)),
                Method(2, "unregister_device", zigbeeAddress), Command(3, "unregister_all_devices"),
                Method(4, "backup_PAN", Zero, Structure(DateTimeOctets, zigbeeAddress, zigbeeBackupEntries)),
                Method(5, "restore_PAN", Structure(zigbeeAddress, zigbeeBackupEntries)), Method(6, "identify_device", zigbeeAddress),
                Method(7, "remove_mirror", Structure(zigbeeAddress, DlmsType.BitString)), Command(8, "update_network_key"),
                Method(9, "update_link_key", zigbeeAddress), Command(10, "create_PAN"), Command(11, "remove_PAN")),
            Class(CosemClassIds.ZigBeeTunnelSetup, 0, "ZigBee Tunnel Setup",
                [Attr(2, "maximum_incoming_transfer_size", DlmsType.UInt16), Attr(3, "maximum_outgoing_transfer_size", DlmsType.UInt16),
                 Attr(4, "protocol_address", Octets(6, true)), Attr(5, "close_tunnel_timeout", DlmsType.UInt16)]),
            Class(CosemClassIds.LoRaWanSetup, 0, "LoRaWAN Setup",
                [Attr(2, "device_class", Range(DlmsType.Enum, 0, 2)), Attr(3, "connection_status", Range(DlmsType.Enum, 0, 4)),
                 Attr(4, "maximum_transmit_power", DlmsType.Int8), Attr(5, "adaptive_data_rate", DlmsType.Boolean), Attr(6, "region", Range(DlmsType.Enum, 0, 11)),
                 Attr(7, "operating_parameters", Structure(DlmsType.UInt32, DlmsType.UInt32, DlmsType.UInt32)),
                 Attr(8, "modem_identification", Structure(DlmsType.VisibleString, DlmsType.VisibleString, DlmsType.VisibleString, DlmsType.VisibleString)),
                 Attr(9, "device_address", DlmsType.UInt32), Attr(10, "join_strategy", DlmsType.Enum),
                 Attr(11, "multicast_parameters", Array(Structure(DlmsType.UInt32, Octets(16), DlmsType.UInt32, DlmsType.UInt32, DlmsType.UInt32, DlmsType.UInt32, Range(DlmsType.Enum, 0, 1), DlmsType.UInt8, DlmsType.UInt32), 4))],
                Command(1, "disconnect"), Method(2, "change_class", Range(DlmsType.Enum, 0, 2)), Method(3, "change_region", Range(DlmsType.Enum, 0, 11))),
            Class(CosemClassIds.LoRaWanDiagnostic, 0, "LoRaWAN Diagnostic",
                [Attr(2, "fault_code", DlmsType.Enum), Attr(3, "unconfirmed_frames_sent", DlmsType.UInt32), Attr(4, "confirmed_frames_sent", DlmsType.UInt32),
                 Attr(5, "unconfirmed_frames_received", DlmsType.UInt32), Attr(6, "confirmed_frames_received", DlmsType.UInt32),
                 Attr(7, "mac_commands_received", DlmsType.UInt32), Attr(8, "failed_mac_responses", DlmsType.UInt32), Attr(9, "ignored_mac_commands", DlmsType.UInt32),
                 Attr(10, "packet_error_rate", DlmsType.Int8), Attr(11, "rx1_rssi", DlmsType.Int8), Attr(12, "rx1_snr", DlmsType.Int8),
                 Attr(13, "rx2_rssi", DlmsType.Int8), Attr(14, "rx2_snr", DlmsType.Int8)], Command(1, "reset_statistics"))
        ];
    }

    private static CosemClassDescriptor[] Manufacturer() =>
    [
        Class(CosemClassIds.ExtendedGsmSettings, 1, "Extended GSM Settings",
            [Attr(2, "reserved_2", DlmsType.Null), Attr(3, "reserved_3", DlmsType.Null), Attr(4, "reserved_4", DlmsType.Null),
             Attr(5, "SIM_selection", Range(DlmsType.UInt8, 0, 4)), Attr(6, "SIM_switch_timeout", DlmsType.UInt16),
             Attr(7, "connection_check_timeout", DlmsType.UInt16), Attr(8, "disconnection_timeout", DlmsType.UInt16),
             Attr(9, "primary_SIM_return_timeout", DlmsType.UInt16), Attr(10, "pre_initialization_commands", Array(DlmsType.OctetString)),
             Attr(11, "post_initialization_commands", Array(DlmsType.OctetString)), Attr(12, "post_registration_commands", Array(DlmsType.OctetString)),
             Attr(13, "allow_list", Array(DlmsType.OctetString))], Command(1, "restart_modem")),
        Class(CosemClassIds.ExtendedGsmDiagnostics, 1, "Extended GSM Diagnostics",
            [Attr(2, "active_network_type", DlmsType.Enum), Attr(3, "active_SIM", DlmsType.UInt8), Attr(4, "MSISDN", Text(16)),
             Attr(5, "ICCID", Text(32)), Attr(6, "IMSI", Text(32)), Attr(7, "IMEI", Text(32))]),
        Class(CosemClassIds.RfSetup, 0, "RF Setup",
            [Attr(2, "MAC_address", OctetsAtMost(16)), Attr(3, "carrier_frequency", DlmsType.UInt32), Attr(4, "channel_number", DlmsType.UInt16),
             Attr(5, "network_access_code", OctetsAtMost(16)), Attr(6, "automatic_channel_selection", DlmsType.Enum),
             Attr(7, "automatic_network_selection", DlmsType.Enum), Attr(8, "modulation", DlmsType.Enum), Attr(9, "data_rate", DlmsType.UInt32),
             Attr(10, "output_power", DlmsType.Int8), Attr(11, "listen_before_talk_threshold", DlmsType.Int8),
             Attr(12, "CRC_type", DlmsType.Enum), Attr(13, "FEC_type", DlmsType.Enum), Attr(14, "compression_type", DlmsType.Enum)], Command(1, "restart_network")),
        Class(CosemClassIds.RfDiagnostics, 0, "RF Diagnostics",
            [Attr(2, "modem_state", Range(DlmsType.Enum, 0, 3)), Attr(3, "RSSI", DlmsType.Int8), Attr(4, "SNR", DlmsType.Int8),
             Attr(5, "packets_received", DlmsType.UInt32), Attr(6, "successful_receive_percent", Range(DlmsType.UInt8, 0, 100)),
             Attr(7, "packets_sent", DlmsType.UInt32), Attr(8, "statistics_start_time", DlmsType.DateTime), Attr(9, "statistics_channel", DlmsType.UInt16)],
            Command(1, "reset_statistics"), Method(2, "select_channel", DlmsType.UInt16)),
        Class(CosemClassIds.CommunicationModuleSetup, 0, "Communication Module Setup",
            [Attr(2, "restart_timeout", Range(DlmsType.UInt32, 0, 86400)), Attr(3, "RS485_1_mode", Range(DlmsType.Enum, 0, 2)),
             Attr(4, "RS485_2_mode", Range(DlmsType.Enum, 0, 2))], Command(1, "restart_module")),
        Class(CosemClassIds.CommunicationModuleDiagnostics, 0, "Communication Module Diagnostics",
            [Attr(2, "diagnostic_information", DlmsType.VisibleString), Attr(3, "diagnostic_start_time", DlmsType.DateTime)],
            Command(1, "reset_diagnostics"), Method(2, "refresh_diagnostics", DlmsType.UInt16)),
        Class(CosemClassIds.ExtendedSimSettings, 0, "Extended SIM Settings",
            [Attr(2, "APN_login", OctetsAtMost(32)), Attr(3, "APN_password", OctetsAtMost(32)),
             Attr(4, "network_type", Range(DlmsType.Enum, 0, 9)), Attr(5, "authentication_type", Range(DlmsType.Enum, 0, 3)),
             Attr(6, "connection_check_IP_address", DlmsType.UInt32), Attr(7, "SMS_center_number", DlmsType.OctetString)])
    ];
}
