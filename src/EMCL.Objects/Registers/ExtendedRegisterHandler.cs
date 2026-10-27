using EMCL.Core.Codecs;
using EMCL.Core.Objects;
using EMCL.Core.Operations;
using EMCL.Core.Values;
using EMCL.Objects.Definitions;
using EMCL.Objects.Handlers;

namespace EMCL.Objects.Registers;

/// <summary>Executes Extended Register v0 reset and application-driven value capture with profile-specific timestamps.</summary>
public sealed class ExtendedRegisterHandler : StoredValueHandler
{
    private readonly TimeProvider timeProvider;
    private readonly ProtocolLimits limits;
    private readonly Func<CosemObject, DlmsValue>? resetValue;

    /// <summary>Creates a local handler without changing the host clock.</summary>
    /// <param name="timeProvider">The source of capture instants; null selects UTC system time.</param>
    /// <param name="resetValue">An instance-specific reset value; null resets initialized numeric values to typed zero.</param>
    /// <param name="limits">Bounds for captured values; null selects validated defaults.</param>
    public ExtendedRegisterHandler(TimeProvider? timeProvider = null, Func<CosemObject, DlmsValue>? resetValue = null, ProtocolLimits? limits = null)
    {
        this.timeProvider = timeProvider ?? TimeProvider.System;
        this.resetValue = resetValue;
        this.limits = ProtocolLimits.Resolve(limits);
    }

    /// <summary>Captures a local measurement and its execution time, optionally replacing status.</summary>
    /// <remarks>This producer-side API bypasses association access checks and serializes against dispatcher operations. A null status preserves the existing status; null-data explicitly replaces it.</remarks>
    /// <param name="cosemObject">An Extended Register v0 instance.</param>
    /// <param name="value">The measurement value permitted by the selected profile.</param>
    /// <param name="status">An optional register status to capture.</param>
    /// <param name="cancellationToken">A token requesting cancellation.</param>
    /// <returns>The capture result; validation failures preserve the previous measurement.</returns>
    public async ValueTask<OperationResult> CaptureAsync(CosemObject cosemObject, DlmsValue value,
        DlmsValue? status = null, CancellationToken cancellationToken = default)
    {
        RegisterValues.RequireClass(cosemObject, CosemClassIds.ExtendedRegister);
        ArgumentNullException.ThrowIfNull(value);
        await cosemObject.OperationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var error = RegisterValues.Validate(cosemObject, ExtendedRegisterMembers.Attributes.Value, value, limits);
            error ??= status == null ? null : RegisterValues.Validate(cosemObject, ExtendedRegisterMembers.Attributes.Status, status, limits);
            if (error != null)
            {
                return new(CosemError.TypeUnmatched, Diagnostic: error);
            }

            var timestamp = RegisterValues.Timestamp(cosemObject, ExtendedRegisterMembers.Attributes.CaptureTime, timeProvider.GetUtcNow());
            cancellationToken.ThrowIfCancellationRequested();
            cosemObject.State.Set(ExtendedRegisterMembers.Attributes.Value, value);
            cosemObject.State.Set(ExtendedRegisterMembers.Attributes.CaptureTime, timestamp);
            if (status != null)
            {
                cosemObject.State.Set(ExtendedRegisterMembers.Attributes.Status, status);
            }

            return OperationResult.Ok();
        }
        finally
        {
            cosemObject.OperationLock.Release();
        }
    }

    /// <inheritdoc />
    public override ValueTask<OperationResult> InvokeAsync(CosemObject cosemObject, CosemOperation operation,
        OperationContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (operation.Index != ExtendedRegisterMembers.Methods.Reset)
        {
            return base.InvokeAsync(cosemObject, operation, context, cancellationToken);
        }

        if (!RegisterValues.IsZeroParameter(operation.Value))
        {
            return ValueTask.FromResult(new OperationResult(CosemError.TypeUnmatched));
        }

        var value = resetValue?.Invoke(cosemObject) ?? RegisterValues.Default(cosemObject, ExtendedRegisterMembers.Attributes.Value);
        if (value == null)
        {
            return ValueTask.FromResult(new OperationResult(CosemError.ObjectUnavailable));
        }

        var error = RegisterValues.Validate(cosemObject, ExtendedRegisterMembers.Attributes.Value, value, limits);
        if (error != null)
        {
            return ValueTask.FromResult(new OperationResult(CosemError.TypeUnmatched, Diagnostic: error));
        }

        var timestamp = RegisterValues.Timestamp(cosemObject, ExtendedRegisterMembers.Attributes.CaptureTime, timeProvider.GetUtcNow());
        cosemObject.State.Set(ExtendedRegisterMembers.Attributes.Value, value);
        cosemObject.State.Set(ExtendedRegisterMembers.Attributes.CaptureTime, timestamp);
        return ValueTask.FromResult(OperationResult.Ok());
    }
}
