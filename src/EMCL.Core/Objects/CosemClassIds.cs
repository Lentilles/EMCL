namespace EMCL.Core.Objects;

/// <summary>Defines the supported interface-class identifiers.</summary>
public static class CosemClassIds
{
    /// <summary>The Data interface-class identifier.</summary>
    public const ushort Data = 1;
    /// <summary>The Register interface-class identifier.</summary>
    public const ushort Register = 3;
    /// <summary>The ExtendedRegister interface-class identifier.</summary>
    public const ushort ExtendedRegister = 4;
    /// <summary>The DemandRegister interface-class identifier.</summary>
    public const ushort DemandRegister = 5;
    /// <summary>The RegisterActivation interface-class identifier.</summary>
    public const ushort RegisterActivation = 6;
    /// <summary>The ProfileGeneric interface-class identifier.</summary>
    public const ushort ProfileGeneric = 7;
    /// <summary>The Clock interface-class identifier.</summary>
    public const ushort Clock = 8;
    /// <summary>The AssociationLn interface-class identifier.</summary>
    public const ushort AssociationLn = 15;

    /// <summary>The ScriptTable interface-class identifier.</summary>
    public const ushort ScriptTable = 9;

    /// <summary>The Schedule interface-class identifier.</summary>
    public const ushort Schedule = 10;

    /// <summary>The SpecialDaysTable interface-class identifier.</summary>
    public const ushort SpecialDaysTable = 11;

    /// <summary>The SapAssignment interface-class identifier.</summary>
    public const ushort SapAssignment = 17;

    /// <summary>The ImageTransfer interface-class identifier.</summary>
    public const ushort ImageTransfer = 18;

    /// <summary>The IecLocalPortSetup interface-class identifier.</summary>
    public const ushort IecLocalPortSetup = 19;

    /// <summary>The ActivityCalendar interface-class identifier.</summary>
    public const ushort ActivityCalendar = 20;

    /// <summary>The RegisterMonitor interface-class identifier.</summary>
    public const ushort RegisterMonitor = 21;

    /// <summary>The SingleActionSchedule interface-class identifier.</summary>
    public const ushort SingleActionSchedule = 22;

    /// <summary>The IecHdlcSetup interface-class identifier.</summary>
    public const ushort IecHdlcSetup = 23;

    /// <summary>The AutoConnect interface-class identifier.</summary>
    public const ushort AutoConnect = 29;

    /// <summary>The PushSetup interface-class identifier.</summary>
    public const ushort PushSetup = 40;

    /// <summary>The TcpUdpSetup interface-class identifier.</summary>
    public const ushort TcpUdpSetup = 41;

    /// <summary>The Ipv4Setup interface-class identifier.</summary>
    public const ushort Ipv4Setup = 42;

    /// <summary>The MacAddressSetup interface-class identifier.</summary>
    public const ushort MacAddressSetup = 43;

    /// <summary>The PPP Setup interface-class identifier.</summary>
    public const ushort PppSetup = 44;

    /// <summary>The GprsModemSetup interface-class identifier.</summary>
    public const ushort GprsModemSetup = 45;

    /// <summary>The GsmDiagnostic interface-class identifier.</summary>
    public const ushort GsmDiagnostic = 47;

    /// <summary>The Ipv6Setup interface-class identifier.</summary>
    public const ushort Ipv6Setup = 48;

    /// <summary>The SecuritySetup interface-class identifier.</summary>
    public const ushort SecuritySetup = 64;

    /// <summary>The Arbitrator interface-class identifier.</summary>
    public const ushort Arbitrator = 68;

    /// <summary>The DisconnectControl interface-class identifier.</summary>
    public const ushort DisconnectControl = 70;

    /// <summary>The Limiter interface-class identifier.</summary>
    public const ushort Limiter = 71;

    /// <summary>The PrimeSscsSetup interface-class identifier.</summary>
    public const ushort PrimeSscsSetup = 80;

    /// <summary>The PrimePhysicalCounters interface-class identifier.</summary>
    public const ushort PrimePhysicalCounters = 81;

    /// <summary>The PrimeMacSetup interface-class identifier.</summary>
    public const ushort PrimeMacSetup = 82;

    /// <summary>The PrimeMacParameters interface-class identifier.</summary>
    public const ushort PrimeMacParameters = 83;

    /// <summary>The PrimeMacCounters interface-class identifier.</summary>
    public const ushort PrimeMacCounters = 84;

    /// <summary>The PrimeNetworkAdministration interface-class identifier.</summary>
    public const ushort PrimeNetworkAdministration = 85;

    /// <summary>The PrimeApplicationIdentification interface-class identifier.</summary>
    public const ushort PrimeApplicationIdentification = 86;

    /// <summary>The G3PlcMacCounters interface-class identifier.</summary>
    public const ushort G3PlcMacCounters = 90;

    /// <summary>The G3PlcMacSetup interface-class identifier.</summary>
    public const ushort G3PlcMacSetup = 91;

    /// <summary>The G3PlcAdaptationSetup interface-class identifier.</summary>
    public const ushort G3PlcAdaptationSetup = 92;

    /// <summary>The ZigBeeStartup interface-class identifier.</summary>
    public const ushort ZigBeeStartup = 101;

    /// <summary>The ZigBeeJoin interface-class identifier.</summary>
    public const ushort ZigBeeJoin = 102;

    /// <summary>The ZigBeeFragmentation interface-class identifier.</summary>
    public const ushort ZigBeeFragmentation = 103;

    /// <summary>The ZigBeeNetworkControl interface-class identifier.</summary>
    public const ushort ZigBeeNetworkControl = 104;

    /// <summary>The ZigBeeTunnelSetup interface-class identifier.</summary>
    public const ushort ZigBeeTunnelSetup = 105;

    /// <summary>The Communication Port Protection interface-class identifier.</summary>
    public const ushort CommunicationPortProtection = 124;

    /// <summary>The LoRaWanSetup interface-class identifier.</summary>
    public const ushort LoRaWanSetup = 128;

    /// <summary>The LoRaWanDiagnostic interface-class identifier.</summary>
    public const ushort LoRaWanDiagnostic = 129;

    /// <summary>The LteMonitoring interface-class identifier.</summary>
    public const ushort LteMonitoring = 151;

    /// <summary>The ExtendedGsmSettings interface-class identifier.</summary>
    public const ushort ExtendedGsmSettings = 5240;

    /// <summary>The ExtendedGsmDiagnostics interface-class identifier.</summary>
    public const ushort ExtendedGsmDiagnostics = 5241;

    /// <summary>The RfSetup interface-class identifier.</summary>
    public const ushort RfSetup = 5242;

    /// <summary>The RfDiagnostics interface-class identifier.</summary>
    public const ushort RfDiagnostics = 5243;

    /// <summary>The CommunicationModuleSetup interface-class identifier.</summary>
    public const ushort CommunicationModuleSetup = 5244;

    /// <summary>The CommunicationModuleDiagnostics interface-class identifier.</summary>
    public const ushort CommunicationModuleDiagnostics = 5245;

    /// <summary>The ExtendedSimSettings interface-class identifier.</summary>
    public const ushort ExtendedSimSettings = 5246;
}
