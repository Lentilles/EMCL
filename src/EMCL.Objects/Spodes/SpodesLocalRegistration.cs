using EMCL.Core.Access;
using EMCL.Core.Codecs;
using EMCL.Core.Objects;
using EMCL.Core.Pipeline;
using EMCL.Objects.Definitions;
using EMCL.Objects.Processing;

namespace EMCL.Objects.Spodes;

/// <summary>Registers the local handlers for a selected SPODES information model.</summary>
public static class SpodesLocalRegistration
{
    /// <summary>Registers handlers for one device and its dispatcher.</summary>
    /// <param name="profile">The selected information model.</param>
    /// <param name="handlers">The device handler registry.</param>
    /// <param name="execute">The operation pipeline used for nested actions.</param>
    /// <param name="options">The application-provided services.</param>
    /// <param name="limits">The resource limits for image and table operations.</param>
    /// <param name="time">The clock used to track elapsed time.</param>
    /// <param name="access">The policy used to check captures from the Push object itself.</param>
    public static void Register(CosemProfile profile, HandlerRegistry handlers, OperationDelegate execute,
        SpodesLocalOptions options, ProtocolLimits limits, TimeProvider time, IAccessPolicy? access = null)
    {
        var tables = new SpodesTablesHandler(limits.MaxCollectionItems);
        var external = new SpodesDeviceHandler(options.Device);
        var relay = new SpodesRelayHandler(options, time);
        foreach (var descriptor in StandardDefinitions.Create(profile))
        {
            EMCL.Core.Objects.ICosemObjectHandler? handler = descriptor.Key.ClassId switch
            {
                CosemClassIds.ScriptTable => new SpodesScriptHandler(execute),
                CosemClassIds.Schedule or CosemClassIds.SpecialDaysTable or CosemClassIds.SapAssignment => tables,
                CosemClassIds.ImageTransfer => new SpodesImageHandler(options.Images, limits),
                CosemClassIds.Arbitrator => new SpodesArbitratorHandler(execute),
                CosemClassIds.DisconnectControl => relay,
                CosemClassIds.ActivityCalendar => new SpodesCalendarHandler(),
                CosemClassIds.PushSetup => new SpodesPushHandler(options.Push, execute, time, access ?? new AssociationAccessPolicy()),
                CosemClassIds.SecuritySetup => new SpodesSecurityHandler(options),
                CosemClassIds.IecLocalPortSetup or CosemClassIds.IecHdlcSetup or CosemClassIds.TcpUdpSetup
                    or >= CosemClassIds.Ipv4Setup and <= CosemClassIds.Ipv6Setup
                    or >= CosemClassIds.PrimeSscsSetup or CosemClassIds.AutoConnect => external,
                _ => null
            };
            if (handler is not null)
            {
                handlers.ForDefaultClass(descriptor.Key, handler);
            }
        }
    }
}
