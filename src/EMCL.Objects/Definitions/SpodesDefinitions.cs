using System.Collections.Immutable;
using EMCL.Core.Descriptors;
using EMCL.Core.Objects;
using EMCL.Core.Values;

using static EMCL.Objects.Definitions.SpodesSchema;

namespace EMCL.Objects.Definitions;

internal static partial class SpodesDefinitions
{
    private static readonly ValueShape ScriptEntry = Structure(DlmsType.UInt16, Array(Structure(Range(DlmsType.Enum, 1, 2), DlmsType.UInt16, Ln, DlmsType.Int8, Any)));
    private static readonly ValueShape ScheduleEntry = Structure(Range(DlmsType.UInt16, 0, 9999), DlmsType.Boolean, Ln, DlmsType.UInt16,
        TimeOctets, DlmsType.UInt16, DlmsType.BitString, DlmsType.BitString, DateOctets, DateOctets);
    private static readonly ValueShape SpecialDay = Structure(DlmsType.UInt16, DateOctets, DlmsType.UInt8);
    private static readonly ValueShape SapEntry = Structure(DlmsType.UInt16, OneOf(Of(DlmsType.OctetString), Of(DlmsType.VisibleString)));
    private static readonly ValueShape Seasons = Array(Structure(DlmsType.OctetString, DateTimeOctets, DlmsType.OctetString));
    private static readonly ValueShape Weeks = Array(Structure(DlmsType.OctetString, DlmsType.UInt8, DlmsType.UInt8, DlmsType.UInt8, DlmsType.UInt8, DlmsType.UInt8, DlmsType.UInt8, DlmsType.UInt8));
    private static readonly ValueShape Days = Array(Structure(DlmsType.UInt8, Array(Structure(TimeOctets, Ln, DlmsType.UInt16))));
    private static readonly ValueShape Capture = Structure(DlmsType.UInt16, Ln, DlmsType.Int8, DlmsType.UInt16);
    private static readonly ValueShape Restriction = OneOf(
        Structure(Range(DlmsType.Enum, 0, 0), DlmsType.Null),
        Structure(Range(DlmsType.Enum, 1, 1), Structure(DateTimeOctets, DateTimeOctets)),
        Structure(Range(DlmsType.Enum, 2, 2), Structure(DlmsType.UInt32, DlmsType.UInt32)));
    private static readonly ValueShape PushProtection = Structure(DlmsType.Enum,
        Structure(DlmsType.OctetString, Octets(8, true), Octets(8, true), DlmsType.OctetString, OneOf(
            Structure(Range(DlmsType.Enum, 0, 0), DlmsType.Enum),
            Structure(Range(DlmsType.Enum, 1, 1), Structure(DlmsType.Enum, DlmsType.OctetString)),
            Structure(Range(DlmsType.Enum, 2, 2), Structure(DlmsType.OctetString, DlmsType.OctetString)))));

    internal static ImmutableArray<CosemClassDescriptor> Create(bool version5)
    {
        ImmutableArray<CosemClassDescriptor> shared =
        [
            DataClassDefinition.Create(), RegisterClassDefinition.Create(),
            ExtendedRegisterClassDefinition.Create(DlmsType.DateTime), DemandRegisterClassDefinition.Create(DlmsType.DateTime),
            RegisterActivationClassDefinition.Create(), ProfileGenericClassDefinition.Create(),
            ClockClassDefinition.Create(version5 ? DlmsType.OctetString : DlmsType.DateTime, DlmsType.DateTime, -720, version5 ? 720 : 840),
            Class(CosemClassIds.ScriptTable, 0, "Script Table", [Attr(2, "scripts", Array(ScriptEntry))], Method(1, "execute", DlmsType.UInt16)),
            Class(CosemClassIds.Schedule, 0, "Schedule", [Attr(2, "entries", Array(ScheduleEntry))],
                Method(1, "enable_disable", Structure(DlmsType.UInt16, DlmsType.UInt16, DlmsType.UInt16, DlmsType.UInt16)),
                Method(2, "insert", ScheduleEntry), Method(3, "delete", Structure(DlmsType.UInt16, DlmsType.UInt16))),
            Class(CosemClassIds.SpecialDaysTable, 0, "Special Days Table", [Attr(2, "entries", Array(SpecialDay))],
                Method(1, "insert", SpecialDay), Method(2, "delete", DlmsType.UInt16)),
            Association(),
            Class(CosemClassIds.SapAssignment, 0, "SAP Assignment", [Attr(2, "SAP_assignment_list", Array(SapEntry))],
                Method(1, "connect_logical_device", SapEntry)),
            ImageTransfer(), LocalPort(), ActivityCalendar(),
            Class(CosemClassIds.RegisterMonitor, 0, "Register Monitor", [Attr(2, "thresholds", Array(Any)),
                Attr(3, "monitored_value", MonitoredReference), Attr(4, "actions", Array(ActionSet))]),
            Class(CosemClassIds.SingleActionSchedule, 0, "Single Action Schedule", [Attr(2, "executed_script", ScriptReference),
                Attr(3, "type", Range(DlmsType.Enum, 1, 5)), Attr(4, "execution_time", Array(Structure(TimeOctets, DateOctets)))]),
            Hdlc(), Push(version5), TcpUdp(version5), CommunicationDefinitions.PppSetup(), Security(version5),
            Arbitrator(), Disconnect(version5), Limiter(), CommunicationDefinitions.PortProtection()
        ];
        return version5 ? [.. shared, .. Version5Network(), .. PowerLine(), .. Radios(), .. Manufacturer()] : shared;
    }

    private static CosemClassDescriptor Association()
    {
        var definition = AssociationClassDefinition.Create(AssociationLnMembers.Version1);
        return definition with
        {
            Attributes = [.. definition.Attributes.Select(attribute => attribute.Index switch
            {
                AssociationLnMembers.Attributes.AssociatedPartnersId => Attr(attribute.Index, attribute.Name,
                    Structure(Range(DlmsType.Int8, 0, 127), Range(DlmsType.UInt16, 0, 0x3fff))),
                AssociationLnMembers.Attributes.AuthenticationMechanismName => Attr(attribute.Index, attribute.Name, DlmsType.OctetString),
                AssociationLnMembers.Attributes.SecuritySetupReference => Attr(attribute.Index, attribute.Name, Ln),
                _ => attribute
            })]
        };
    }

    private static CosemClassDescriptor ImageTransfer() => Class(CosemClassIds.ImageTransfer, 0, "Image Transfer",
        [Attr(2, "image_block_size", Range(DlmsType.UInt32, 1, uint.MaxValue)), Attr(3, "image_transferred_blocks_status", DlmsType.BitString),
         Attr(4, "image_first_not_transferred_block_number", DlmsType.UInt32), Attr(5, "image_transfer_enabled", DlmsType.Boolean),
         Attr(6, "image_transfer_status", Range(DlmsType.Enum, 0, 7)),
         Attr(7, "image_to_activate_info", Array(Structure(DlmsType.UInt32, DlmsType.OctetString, DlmsType.OctetString)))],
        Method(1, "image_transfer_initiate", Structure(DlmsType.OctetString, Range(DlmsType.UInt32, 1, uint.MaxValue))),
        Method(2, "image_block_transfer", Structure(DlmsType.UInt32, DlmsType.OctetString)), Command(3, "image_verify"), Command(4, "image_activate"));

    private static CosemClassDescriptor LocalPort() => Class(CosemClassIds.IecLocalPortSetup, 1, "IEC Local Port Setup",
        [Attr(2, "default_mode", Range(DlmsType.Enum, 0, 2)), Attr(3, "default_baud", Range(DlmsType.Enum, 0, 9)),
         Attr(4, "proposed_baud", Range(DlmsType.Enum, 0, 9)), Attr(5, "response_time", Range(DlmsType.Enum, 0, 1)),
         Attr(6, "device_address", DlmsType.OctetString), Attr(7, "password_1", DlmsType.OctetString),
         Attr(8, "password_2", DlmsType.OctetString), Attr(9, "password_5", DlmsType.OctetString)]);

    private static CosemClassDescriptor Hdlc() => Class(CosemClassIds.IecHdlcSetup, 1, "IEC HDLC Setup",
        [Attr(2, "communication_speed", Range(DlmsType.Enum, 0, 9)), Attr(3, "window_size_transmit", Range(DlmsType.UInt8, 1, 7)),
         Attr(4, "window_size_receive", Range(DlmsType.UInt8, 1, 7)), Attr(5, "maximum_info_length_transmit", Range(DlmsType.UInt16, 32, 2030)),
         Attr(6, "maximum_info_length_receive", Range(DlmsType.UInt16, 32, 2030)),
         Attr(7, "inter_octet_time_out", Range(DlmsType.UInt16, 20, 6000)), Attr(8, "inactivity_time_out", Range(DlmsType.UInt16, 0, 120)),
         Attr(9, "device_address", Range(DlmsType.UInt16, 0, 0x3fff))]);

    private static CosemClassDescriptor ActivityCalendar() => Class(CosemClassIds.ActivityCalendar, 0, "Activity Calendar",
        [Attr(2, "calendar_name_active", DlmsType.OctetString), Attr(3, "season_profile_active", Seasons),
         Attr(4, "week_profile_table_active", Weeks), Attr(5, "day_profile_table_active", Days),
         Attr(6, "calendar_name_passive", DlmsType.OctetString), Attr(7, "season_profile_passive", Seasons),
         Attr(8, "week_profile_table_passive", Weeks), Attr(9, "day_profile_table_passive", Days),
         Attr(10, "activate_passive_calendar_time", DlmsType.DateTime)], Command(1, "activate_passive_calendar"));

    private static CosemClassDescriptor Push(bool version5) => Class(CosemClassIds.PushSetup, version5 ? (byte)3 : (byte)2, "Push Setup",
        [Attr(2, "push_object_list", Array(Structure(DlmsType.UInt16, Ln, DlmsType.Int8, DlmsType.UInt16, Restriction, Array(Capture)))),
         Attr(3, "send_destination_and_method", Structure(DlmsType.Enum, DlmsType.OctetString, DlmsType.Enum)),
         Attr(4, "communication_window", Array(Structure(DlmsType.DateTime, DlmsType.DateTime))),
         Attr(5, "randomisation_start_interval", DlmsType.UInt16), Attr(6, "number_of_retries", DlmsType.UInt8),
         Attr(7, "repetition_delay", Structure(DlmsType.UInt16, DlmsType.UInt16, DlmsType.UInt16)), Attr(8, "port_reference", Octets(6, true)),
         Attr(9, "push_client_SAP", DlmsType.Int8), Attr(10, "push_protection_parameters", Array(PushProtection)),
         Attr(11, "push_operation_method", Range(DlmsType.Enum, 0, 2)),
         Attr(12, "confirmation_parameters", Structure(DlmsType.DateTime, DlmsType.UInt32)), Attr(13, "last_confirmation_date_time", DlmsType.DateTime)],
        Command(1, "push"), Command(2, "reset"));

    private static CosemClassDescriptor TcpUdp(bool version5) => Class(CosemClassIds.TcpUdpSetup, 0, "TCP/UDP Setup",
        [Attr(2, "TCP_UDP_port", Range(DlmsType.UInt16, version5 ? 1024 : 0, ushort.MaxValue)),
         Attr(3, "IP_reference", Ln), Attr(4, "MSS", DlmsType.UInt16), Attr(5, "nb_of_sim_conn", DlmsType.UInt8),
         Attr(6, "inactivity_time_out", DlmsType.UInt16)]);

    private static CosemClassDescriptor Security(bool version5) => Class(CosemClassIds.SecuritySetup, 1, "Security Setup",
        [Attr(2, "security_policy", Range(DlmsType.UInt8, 0, 252)), Attr(3, "security_suite", OneOf(
             Range(DlmsType.Enum, 0, 0), version5 ? Range(DlmsType.Enum, 3, 3) : Range(DlmsType.Enum, 3, 4))),
         Attr(4, "client_system_title", Octets(8, true)), Attr(5, "server_system_title", Octets(8)),
         Attr(6, "certificates", Array(Structure(DlmsType.Enum, DlmsType.Enum, DlmsType.OctetString, DlmsType.OctetString, DlmsType.OctetString, DlmsType.OctetString)))],
        Method(1, "security_activate", DlmsType.Enum), Method(2, "key_transfer", Array(Structure(Range(DlmsType.Enum, 0, 3), DlmsType.OctetString))),
        Method(3, "key_agreement", Array(Structure(DlmsType.Enum, DlmsType.OctetString)), Array(Structure(DlmsType.Enum, DlmsType.OctetString))),
        Method(4, "generate_key_pair", DlmsType.Enum), Method(5, "generate_certificate_request", DlmsType.Enum, Of(DlmsType.OctetString)),
        Method(6, "import_certificate", DlmsType.OctetString),
        Method(7, "export_certificate", CertificateIdentification(), Of(DlmsType.OctetString)),
        Method(8, "remove_certificate", CertificateIdentification()));

    private static ValueShape CertificateIdentification() => OneOf(
        Structure(Range(DlmsType.Enum, 0, 0), Structure(DlmsType.Enum, DlmsType.Enum, Octets(8))),
        Structure(Range(DlmsType.Enum, 1, 1), Structure(DlmsType.OctetString, DlmsType.OctetString)));

    private static CosemClassDescriptor Arbitrator() => Class(CosemClassIds.Arbitrator, 0, "Arbitrator",
        [Attr(2, "actions", Array(ScriptReference, byte.MaxValue)), Attr(3, "permissions_table", Array(DlmsType.BitString, 16)),
         Attr(4, "weightings_table", Array(Array(DlmsType.UInt16), 16)), Attr(5, "most_recent_requests_table", Array(DlmsType.BitString, 16)),
         Attr(6, "last_outcome", DlmsType.UInt8)], Method(1, "request_action", Structure(DlmsType.UInt8, DlmsType.BitString)), Command(2, "reset"));

    private static CosemClassDescriptor Disconnect(bool version5)
    {
        CosemAttributeDescriptor[] attributes =
        [
            Attr(2, "output_state", DlmsType.Boolean), Attr(3, "control_state", version5
                ? OneOf(Range(DlmsType.Enum, 0, 2), Range(DlmsType.Enum, 129, 131)) : Range(DlmsType.Enum, 0, 2)),
            Attr(4, "control_mode", version5 ? Range(DlmsType.Enum, 0, 7) : OneOf(Range(DlmsType.Enum, 0, 6), Range(DlmsType.Enum, 129, 134)))
        ];
        CosemMethodDescriptor[] methods = [Command(1, "remote_disconnect"), Command(2, "remote_reconnect")];
        return version5
            ? Class(CosemClassIds.DisconnectControl, 2, "Disconnect Control", [.. attributes, Attr(DisconnectControlMembers.DelayedMode, "delayed_disconnect_mode", DlmsType.Boolean)],
                [.. methods, Command(3, "manual_disconnect"), Command(4, "manual_reconnect"),
                 Method(DisconnectControlMembers.InitializeDelayed, "initialize_delayed_disconnect",
                     Structure(Range(DlmsType.UInt8, 24, 168), Range(DlmsType.UInt8, 24, 168), Range(DlmsType.UInt16, 168, 336)), Of(DlmsType.UInt64)),
                 Method(DisconnectControlMembers.ConfirmDelayed, "confirm_delayed_disconnect", Structure(DlmsType.UInt64, DlmsType.OctetString)),
                 Method(DisconnectControlMembers.BlockControl, "block_control", Range(DlmsType.UInt16, 168, 336))])
            : Class(CosemClassIds.DisconnectControl, 0, "Disconnect Control", attributes, methods);
    }

    private static CosemClassDescriptor Limiter() => Class(CosemClassIds.Limiter, 0, "Limiter",
        [Attr(2, "monitored_value", MonitoredReference), Attr(3, "threshold_active", Any), Attr(4, "threshold_normal", Any),
         Attr(5, "threshold_emergency", Any), Attr(6, "min_over_threshold_duration", DlmsType.UInt32),
         Attr(7, "min_under_threshold_duration", DlmsType.UInt32), Attr(8, "emergency_profile", Structure(DlmsType.UInt16, DateTimeOctets, DlmsType.UInt32)),
         Attr(9, "emergency_profile_group_id_list", Array(DlmsType.UInt16)), Attr(10, "emergency_profile_active", DlmsType.Boolean),
         Attr(11, "actions", ActionSet)]);
}
