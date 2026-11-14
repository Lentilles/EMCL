using EMCL.Core.Objects;
using EMCL.Core.Operations;
using EMCL.Protocol.Security;

namespace EMCL.Objects.Spodes;

/// <summary>Connects the local SPODES model to application-owned services.</summary>
public sealed class SpodesLocalOptions
{
    /// <summary>Gets the adapter for device configuration, diagnostic methods and physical relay changes.</summary>
    public ISpodesDeviceAdapter? Device
    {
        get; init;
    }

    /// <summary>Gets the service that verifies and activates an image.</summary>
    public ISpodesImageTarget? Images
    {
        get; init;
    }
    /// <summary>Gets the service that sends Push notifications for the local device.</summary>
    public ISpodesPushTransport? Push
    {
        get; init;
    }

    /// <summary>Gets the protocol used to authenticate and unwrap Security Setup key batches.</summary>
    public ISecurityKeyTransfer? KeyTransfer
    {
        get; init;
    }

    /// <summary>Gets the application's durable Security Setup key store.</summary>
    public ISpodesSecurityStore? SecurityStore
    {
        get; init;
    }

    /// <summary>Gets the callback that schedules a state change after a successful ACTION response is sent.</summary>
    public Action<LogicalName, CosemOperation, Func<CancellationToken, ValueTask>>? AfterResponse
    {
        get; init;
    }

    /// <summary>Gets the application mapping from a relay to its device logical name.</summary>
    public Func<CosemObject, ReadOnlyMemory<byte>>? ResolveDeviceName
    {
        get; init;
    }

    /// <summary>Gets the callback that writes one audit record for a relay control operation.</summary>
    public Func<LogicalName, CosemOperation, OperationResult, CancellationToken, ValueTask>? RelayAudit
    {
        get; init;
    }

    /// <summary>Gets the predicate for operations requiring the configurator association.</summary>
    public Func<OperationContext, bool> IsConfigurator { get; init; } = context => context.Association is null;

    /// <summary>Gets the predicate for operations requiring a physical optical-port connection.</summary>
    public Func<OperationContext, bool> IsOpticalPort { get; init; } = _ => false;
}
