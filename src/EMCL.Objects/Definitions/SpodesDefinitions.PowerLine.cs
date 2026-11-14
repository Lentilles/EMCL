using EMCL.Core.Descriptors;
using EMCL.Core.Objects;
using EMCL.Core.Values;
using static EMCL.Objects.Definitions.SpodesSchema;

namespace EMCL.Objects.Definitions;

internal static partial class SpodesDefinitions
{
    private static CosemClassDescriptor[] PowerLine() =>
    [
        Class(CosemClassIds.PrimeSscsSetup, 0, "61334-4-32 LLC SSCS Setup",
            [Attr(2, "service_node_address", DlmsType.UInt16), Attr(3, "base_node_address", DlmsType.UInt16)], Command(1, "reset")),
        Class(CosemClassIds.PrimePhysicalCounters, 0, "PRIME Physical Layer Counters",
            [Attr(2, "phy_stats_crc_incorrect_count", DlmsType.UInt16), Attr(3, "phy_stats_crc_fail_count", DlmsType.UInt16),
             Attr(4, "phy_stats_tx_drop_count", DlmsType.UInt16), Attr(5, "phy_stats_rx_drop_count", DlmsType.UInt16)], Command(1, "reset")),
        Class(CosemClassIds.PrimeMacSetup, 0, "PRIME MAC Setup",
            [Attr(2, "mac_min_switch_search_time", Range(DlmsType.UInt8, 16, 32)), Attr(3, "mac_max_promotion_pdu", Range(DlmsType.UInt8, 1, 4)),
             Attr(4, "mac_promotion_pdu_tx_period", Range(DlmsType.UInt8, 2, 8)), Attr(5, "mac_beacons_per_frame", Range(DlmsType.UInt8, 1, 5)),
             Attr(6, "mac_scp_max_tx_attempts", Range(DlmsType.UInt8, 2, 5)), Attr(7, "mac_ctl_re_tx_timer", Range(DlmsType.UInt8, 2, 20)),
             Attr(8, "mac_max_ctl_re_tx", Range(DlmsType.UInt8, 3, 5))]),
        Class(CosemClassIds.PrimeMacParameters, 0, "PRIME MAC Functional Parameters",
            [Attr(2, "mac_LNID", DlmsType.Int16), Attr(3, "mac_LSID", DlmsType.UInt8), Attr(4, "mac_SID", DlmsType.UInt8), Attr(5, "mac_SNA", Octets(6)),
             Attr(6, "mac_state", Range(DlmsType.Enum, 0, 3)), Attr(7, "mac_scp_length", DlmsType.Int16),
             Attr(8, "mac_node_hierarchy_level", Range(DlmsType.UInt8, 0, 63)), Attr(9, "mac_beacon_slot_count", Range(DlmsType.UInt8, 0, 7)),
             Attr(10, "mac_beacon_rx_slot", Range(DlmsType.UInt8, 0, 7)), Attr(11, "mac_beacon_tx_slot", Range(DlmsType.UInt8, 0, 7)),
             Attr(12, "mac_beacon_rx_frequency", Range(DlmsType.UInt8, 0, 31)), Attr(13, "mac_beacon_tx_frequency", Range(DlmsType.UInt8, 0, 31)),
             Attr(14, "mac_capabilities", DlmsType.UInt16)]),
        Class(CosemClassIds.PrimeMacCounters, 0, "PRIME MAC Counters",
            [Attr(2, "mac_tx_data_pkt_count", DlmsType.UInt32), Attr(3, "mac_rx_data_pkt_count", DlmsType.UInt32), Attr(4, "mac_tx_ctrl_pkt_count", DlmsType.UInt32),
             Attr(5, "mac_rx_ctrl_pkt_count", DlmsType.UInt32), Attr(6, "mac_csma_fail_count", DlmsType.UInt32), Attr(7, "mac_csma_ch_busy_count", DlmsType.UInt32)],
            Command(1, "reset")),
        Class(CosemClassIds.PrimeNetworkAdministration, 0, "PRIME MAC Network Administration Data",
            [Attr(2, "mac_list_multicast_entries", Array(Structure(DlmsType.Int8, DlmsType.Int16))), Attr(3, "mac_list_switch_table", Array(DlmsType.Int16)),
             Attr(4, "mac_list_direct_table", Array(Structure(DlmsType.Int16, DlmsType.Int16, DlmsType.Int16, DlmsType.Int16, DlmsType.Int16, DlmsType.Int16, Octets(6)))),
             Attr(5, "mac_list_available_switches", Array(Structure(Octets(6), DlmsType.Int16, DlmsType.Int8, DlmsType.Int8, DlmsType.Int8))),
             Attr(6, "mac_list_phy_comm", Array(Structure(DlmsType.OctetString, DlmsType.Int8, DlmsType.Int8, DlmsType.Int8, DlmsType.Int8, DlmsType.Int8, DlmsType.Int8, DlmsType.Int8, DlmsType.Int8)))], Command(1, "reset")),
        Class(CosemClassIds.PrimeApplicationIdentification, 0, "PRIME Application Identification",
            [Attr(2, "firmware_version", OctetsAtMost(128)), Attr(3, "vendor_Id", DlmsType.UInt16), Attr(4, "product_Id", DlmsType.UInt16)]),
        Class(CosemClassIds.G3PlcMacCounters, 1, "G3-PLC MAC Layer Counters",
            [Attr(2, "mac_Tx_data_packet_count", DlmsType.UInt32), Attr(3, "mac_Rx_data_packet_count", DlmsType.UInt32),
             Attr(4, "mac_Tx_cmd_packet_count", DlmsType.UInt32), Attr(5, "mac_Rx_cmd_packet_count", DlmsType.UInt32),
             Attr(6, "mac_CSMA_fail_count", DlmsType.UInt32), Attr(7, "mac_CSMA_no_ACK_count", DlmsType.UInt32), Attr(8, "mac_bad_CRC_count", DlmsType.UInt32),
             Attr(9, "mac_Tx_data_broadcast_count", DlmsType.UInt32), Attr(10, "mac_Rx_data_broadcast_count", DlmsType.UInt32)], Command(1, "reset")),
        G3Mac(), G3Adaptation()
    ];

    private static CosemClassDescriptor G3Mac()
    {
        var neighbours = Array(Structure(DlmsType.UInt16, DlmsType.Boolean, DlmsType.BitString, DlmsType.Enum, DlmsType.Int8, DlmsType.Enum, DlmsType.BitString, DlmsType.UInt8, DlmsType.UInt8, DlmsType.UInt8));
        var pos = Array(Structure(DlmsType.UInt16, DlmsType.UInt8, DlmsType.UInt8));
        return Class(CosemClassIds.G3PlcMacSetup, 4, "G3-PLC MAC Setup",
            [Attr(2, "mac_short_address", DlmsType.UInt16), Attr(3, "mac_RC_coord", DlmsType.UInt16), Attr(4, "mac_PAN_id", DlmsType.UInt16),
             Attr(5, "mac_key_table", Array(Structure(DlmsType.UInt8, Octets(16)), 2)), Attr(6, "mac_frame_counter", DlmsType.UInt32),
             Attr(7, "mac_tone_mask", DlmsType.BitString), Attr(8, "mac_TMR_TTL", Range(DlmsType.UInt8, 1, 255)),
             Attr(9, "mac_max_frame_retries", Range(DlmsType.UInt8, 0, 50)), Attr(10, "mac_POS_table_entry_TTL", Range(DlmsType.UInt8, 1, 255)),
             Attr(11, "mac_neighbour_table", neighbours), Attr(12, "mac_high_priority_window_size", Range(DlmsType.UInt8, 1, 7)),
             Attr(13, "mac_CSMA_fairness_limit", DlmsType.UInt8), Attr(14, "mac_beacon_randomization_window_length", Range(DlmsType.UInt8, 1, 255)),
             Attr(15, "mac_A", Range(DlmsType.UInt8, 3, 20)), Attr(16, "mac_K", Range(DlmsType.UInt8, 1, 255)),
             Attr(17, "mac_min_CW_attempts", Range(DlmsType.UInt8, 1, 255)), Attr(18, "mac_cenelec_legacy_mode", DlmsType.UInt8),
             Attr(19, "mac_FCC_legacy_mode", DlmsType.UInt8), Attr(20, "mac_max_BE", Range(DlmsType.UInt8, 0, 14)),
             Attr(21, "mac_max_CSMA_backoffs", Range(DlmsType.UInt8, 0, 100)), Attr(22, "mac_min_BE", Range(DlmsType.UInt8, 0, 14)),
             Attr(23, "mac_broadcast_max_CW_enabled", DlmsType.Boolean), Attr(24, "mac_transmit_atten", Range(DlmsType.UInt8, 0, 25)),
             Attr(25, "mac_POS_table", pos), Attr(26, "mac_duplicate_detection_TTL", DlmsType.UInt8),
             Attr(27, "mac_POS_recent_entry_threshold", Range(DlmsType.UInt8, 1, 255)), Attr(28, "mac_POS_recent_entries", DlmsType.UInt16),
             Attr(29, "mac_preamble_length", OneOf(Range(DlmsType.UInt8, 8, 8), Range(DlmsType.UInt8, 12, 12)))],
            Method(1, "mac_get_neighbour_table_entry", DlmsType.UInt16, neighbours), Method(2, "mac_get_POS_table_entry", DlmsType.UInt16, pos));
    }

    private static CosemClassDescriptor G3Adaptation() => Class(CosemClassIds.G3PlcAdaptationSetup, 4, "G3-PLC 6LoWPAN Adaptation Layer Setup",
        [Attr(2, "adp_max_hops", Range(DlmsType.UInt8, 1, 14)), Attr(3, "adp_weak_LQI_value", DlmsType.UInt8),
         Attr(4, "adp_security_level", Range(DlmsType.UInt8, 0, 5)), Attr(5, "adp_prefix_table", Array(DlmsType.UInt8)),
         Attr(6, "adp_routing_configuration", Array(Structure(DlmsType.UInt8, DlmsType.UInt16, DlmsType.UInt8, DlmsType.UInt8, DlmsType.UInt8, DlmsType.UInt8, DlmsType.UInt8, DlmsType.UInt8,
             DlmsType.UInt8, DlmsType.UInt8, DlmsType.UInt16, DlmsType.Boolean, DlmsType.Boolean, DlmsType.UInt8, DlmsType.UInt8))),
         Attr(7, "adp_broadcast_log_table_entry_TTL", Range(DlmsType.UInt16, 1, 65535)),
         Attr(8, "adp_routing_table", Array(Structure(DlmsType.UInt16, DlmsType.UInt16, DlmsType.UInt16, DlmsType.UInt8, DlmsType.UInt8, DlmsType.UInt16))),
         Attr(9, "adp_context_information_table", Array(Structure(DlmsType.BitString, Range(DlmsType.UInt8, 0, 128), OctetsAtMost(16), DlmsType.Boolean, DlmsType.UInt16))),
         Attr(10, "adp_blacklist_table", Array(Structure(DlmsType.UInt16, DlmsType.UInt16))),
         Attr(11, "adp_broadcast_log_table", Array(Structure(DlmsType.UInt16, DlmsType.UInt8, DlmsType.UInt16))), Attr(12, "adp_group_table", Array(DlmsType.UInt16)),
         Attr(13, "adp_max_join_wait_time", Range(DlmsType.UInt16, 1, 1023)), Attr(14, "adp_path_discovery_time", Range(DlmsType.UInt8, 1, 255)),
         Attr(15, "adp_active_key_index", Range(DlmsType.UInt8, 0, 1)), Attr(16, "adp_metric_type", Range(DlmsType.UInt8, 0, 15)),
         Attr(17, "adp_coord_short_address", Range(DlmsType.UInt16, 0, 32767)), Attr(18, "adp_disable_default_routing", DlmsType.Boolean),
         Attr(19, "adp_device_type", Range(DlmsType.Enum, 0, 2)), Attr(20, "adp_default_coord_route_enabled", DlmsType.Boolean),
         Attr(21, "adp_destination_address_set", Array(DlmsType.UInt16)), Attr(22, "adp_low_LQI_value", DlmsType.UInt8),
         Attr(23, "adp_high_LQI_value", DlmsType.UInt8), Attr(24, "adp_delay_low_LQI", DlmsType.UInt16), Attr(25, "adp_delay_high_LQI", DlmsType.UInt16),
         Attr(26, "adp_RREQ_jitter_low_LQI", DlmsType.UInt8), Attr(27, "adp_RREQ_jitter_high_LQI", DlmsType.UInt8),
         Attr(28, "adp_trickle_data_enabled", DlmsType.Boolean), Attr(29, "adp_trickle_LQI_threshold_low", DlmsType.UInt8),
         Attr(30, "adp_trickle_step", Range(DlmsType.UInt8, 1, 255)), Attr(31, "adp_trickle_Imin", Range(DlmsType.UInt16, 100, 5000)),
         Attr(32, "adp_trickle_max_Ki", Range(DlmsType.UInt8, 1, 5)), Attr(33, "adp_trickle_adaptive_Imin", DlmsType.Boolean),
         Attr(34, "adp_trickle_adaptive_Ki", DlmsType.Boolean), Attr(35, "adp_cluster_trickle_enabled", DlmsType.Boolean),
         Attr(36, "adp_cluster_min_LQI", DlmsType.UInt8), Attr(37, "adp_cluster_trickle_K", Range(DlmsType.UInt8, 1, 10)),
         Attr(38, "adp_cluster_RREQ_route_cost", DlmsType.UInt8), Attr(39, "adp_cluster_trickle_I", Range(DlmsType.UInt16, 0, 4096)),
         Attr(40, "adp_trickle_LQI_threshold_high", DlmsType.UInt8)]);
}
