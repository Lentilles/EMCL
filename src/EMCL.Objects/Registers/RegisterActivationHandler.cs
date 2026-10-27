using EMCL.Core.Codecs;
using EMCL.Core.Objects;
using EMCL.Core.Operations;
using EMCL.Core.Values;
using EMCL.Objects.Catalog;
using EMCL.Objects.Definitions;
using EMCL.Objects.Handlers;
using EMCL.Objects.Internal;

namespace EMCL.Objects.Registers;

/// <summary>Maintains Register Activation v0 assignments, named masks, and the selected mask.</summary>
/// <remarks>Assignment positions are one-based unsigned8 indices, limiting this implementation to 255 assignments. The application uses IsEnabledAsync when acquiring measurements. Deleting the selected mask is refused until another mask is selected.</remarks>
public sealed class RegisterActivationHandler : StoredValueHandler
{
    private readonly ObjectCatalog catalog;
    private readonly ProtocolLimits limits;

    /// <summary>Creates an activation handler for one logical device.</summary>
    /// <param name="catalog">The catalog used to resolve assigned registers.</param>
    /// <param name="limits">Bounds for stored arrays and encoded values; null selects validated defaults.</param>
    public RegisterActivationHandler(ObjectCatalog catalog, ProtocolLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        this.catalog = catalog;
        this.limits = ProtocolLimits.Resolve(limits);
    }

    /// <summary>Checks whether the selected mask enables a register for application-driven measurement.</summary>
    /// <remarks>Unassigned registers are enabled. Assigned registers without a selected mask are disabled. This local query bypasses association access checks and does not change register values.</remarks>
    /// <param name="cosemObject">A Register Activation v0 instance.</param>
    /// <param name="registerName">The logical name of a Register, Extended Register, or Demand Register in the catalog.</param>
    /// <param name="cancellationToken">A token requesting cancellation.</param>
    /// <returns>True if the register is unassigned or any of its assignment positions is selected.</returns>
    /// <exception cref="ArgumentException">The requested register is absent or has an unsupported class.</exception>
    /// <exception cref="InvalidOperationException">Stored activation configuration is inconsistent.</exception>
    public async ValueTask<bool> IsEnabledAsync(CosemObject cosemObject, LogicalName registerName,
        CancellationToken cancellationToken = default)
    {
        RegisterValues.RequireClass(cosemObject, CosemClassIds.RegisterActivation);
        if (catalog.Find(registerName) is not { } register || register.Descriptor.Key.ClassId is not (CosemClassIds.Register or CosemClassIds.ExtendedRegister or CosemClassIds.DemandRegister))
        {
            throw new ArgumentException("The register must exist in the logical-device catalog.", nameof(registerName));
        }

        await cosemObject.OperationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var assignments = StoredArray(cosemObject, RegisterActivationMembers.Attributes.RegisterAssignment);
            var masks = StoredArray(cosemObject, RegisterActivationMembers.Attributes.MaskList);
            var active = ActiveMask(cosemObject);
            var result = Validate(cosemObject, assignments, masks, active);
            if (result.Error != CosemError.Success)
            {
                throw new InvalidOperationException(result.Diagnostic ?? "Invalid register activation configuration.");
            }

            var positions = assignments.Items.Select((entry, index) => (entry, index))
                .Where(item => Name(item.entry) == registerName).Select(item => item.index + 1).ToHashSet();
            if (positions.Count == 0)
            {
                return true;
            }

            var selected = active == null ? null : masks.Items.First(mask => SameName(mask.Items[RegisterStructures.Mask.Name], active));
            return selected != null && selected.Items[RegisterStructures.Mask.Indices].Items.Any(index => positions.Contains((int)index.AsUInt64()));
        }
        finally
        {
            cosemObject.OperationLock.Release();
        }
    }

    /// <inheritdoc />
    public override ValueTask<OperationResult> ReadAsync(CosemObject cosemObject, CosemOperation operation,
        OperationContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return operation.Index is RegisterActivationMembers.Attributes.RegisterAssignment or RegisterActivationMembers.Attributes.MaskList && operation.Selection == null
            ? ValueTask.FromResult(OperationResult.Ok(StoredArray(cosemObject, operation.Index)))
            : base.ReadAsync(cosemObject, operation, context, cancellationToken);
    }

    /// <inheritdoc />
    public override ValueTask<OperationResult> WriteAsync(CosemObject cosemObject, CosemOperation operation,
        OperationContext context, CancellationToken cancellationToken)
    {
        if (operation.Index is not (RegisterActivationMembers.Attributes.RegisterAssignment or RegisterActivationMembers.Attributes.MaskList or RegisterActivationMembers.Attributes.ActiveMask) || operation.Selection != null)
        {
            return base.WriteAsync(cosemObject, operation, context, cancellationToken);
        }

        cancellationToken.ThrowIfCancellationRequested();
        var value = operation.Value ?? throw new ArgumentException("Missing value.", nameof(operation));
        var assignments = operation.Index == RegisterActivationMembers.Attributes.RegisterAssignment ? value : StoredArray(cosemObject, RegisterActivationMembers.Attributes.RegisterAssignment);
        var masks = operation.Index == RegisterActivationMembers.Attributes.MaskList ? value : StoredArray(cosemObject, RegisterActivationMembers.Attributes.MaskList);
        var active = operation.Index == RegisterActivationMembers.Attributes.ActiveMask ? value : ActiveMask(cosemObject);
        var result = Validate(cosemObject, assignments, masks, active);
        if (result.Error == CosemError.Success)
        {
            cosemObject.State.Set(operation.Index, value);
        }

        return ValueTask.FromResult(result);
    }

    /// <inheritdoc />
    public override ValueTask<OperationResult> InvokeAsync(CosemObject cosemObject, CosemOperation operation,
        OperationContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (operation.Index is not (RegisterActivationMembers.Methods.AddRegister or RegisterActivationMembers.Methods.AddMask or RegisterActivationMembers.Methods.DeleteMask))
        {
            return base.InvokeAsync(cosemObject, operation, context, cancellationToken);
        }

        var value = operation.Value ?? throw new ArgumentException("Missing method parameter.", nameof(operation));
        var assignments = StoredArray(cosemObject, RegisterActivationMembers.Attributes.RegisterAssignment);
        var masks = StoredArray(cosemObject, RegisterActivationMembers.Attributes.MaskList);
        var active = ActiveMask(cosemObject);
        sbyte changedIndex;
        if (operation.Index == RegisterActivationMembers.Methods.AddRegister)
        {
            assignments = new(DlmsType.Array, assignments.Items.Add(value));
            changedIndex = RegisterActivationMembers.Attributes.RegisterAssignment;
        }
        else if (operation.Index == RegisterActivationMembers.Methods.AddMask)
        {
            // Validate the proposed shape before examining its name.
            var shapeError = RegisterValues.Validate(cosemObject, RegisterActivationMembers.Attributes.MaskList, DlmsValue.Array(value), limits);
            if (shapeError != null)
            {
                return ValueTask.FromResult(new OperationResult(CosemError.TypeUnmatched, Diagnostic: shapeError));
            }

            int position = FindMask(masks, value.Items[RegisterStructures.Mask.Name]);
            masks = new(DlmsType.Array, position < 0 ? masks.Items.Add(value) : masks.Items.SetItem(position, value));
            changedIndex = RegisterActivationMembers.Attributes.MaskList;
        }
        else
        {
            if (value.Type != DlmsType.OctetString)
            {
                return ValueTask.FromResult(new OperationResult(CosemError.TypeUnmatched));
            }

            int position = FindMask(masks, value);
            if (position < 0)
            {
                return ValueTask.FromResult(new OperationResult(CosemError.ObjectUndefined));
            }

            if (active != null && SameName(active, value))
            {
                return ValueTask.FromResult(new OperationResult(CosemError.TemporaryFailure,
                    Diagnostic: "Select another mask before deleting the active mask."));
            }

            masks = new(DlmsType.Array, masks.Items.RemoveAt(position));
            changedIndex = RegisterActivationMembers.Attributes.MaskList;
        }

        var result = Validate(cosemObject, assignments, masks, active);
        if (result.Error == CosemError.Success)
        {
            cosemObject.State.Set(changedIndex, changedIndex == RegisterActivationMembers.Attributes.RegisterAssignment ? assignments : masks);
        }

        return ValueTask.FromResult(result);
    }

    private OperationResult Validate(CosemObject cosemObject, DlmsValue assignments, DlmsValue masks, DlmsValue? active)
    {
        var error = RegisterValues.Validate(cosemObject, RegisterActivationMembers.Attributes.RegisterAssignment, assignments, limits)
            ?? RegisterValues.Validate(cosemObject, RegisterActivationMembers.Attributes.MaskList, masks, limits)
            ?? (active == null ? null : RegisterValues.Validate(cosemObject, RegisterActivationMembers.Attributes.ActiveMask, active, limits));
        if (error != null)
        {
            return new(CosemError.TypeUnmatched, Diagnostic: error);
        }

        if (assignments.Items.Length > byte.MaxValue)
        {
            return new(CosemError.ScopeOfAccessViolated, Diagnostic: "At most 255 register assignments are supported.");
        }

        foreach (var entry in assignments.Items)
        {
            var classId = entry.Items[RegisterStructures.Assignment.ClassId].AsUInt64();
            var register = catalog.Find(Name(entry));
            if (register == null)
            {
                return new(CosemError.ObjectUndefined, Diagnostic: "An assigned register is absent from the catalog.");
            }

            if (classId is not (CosemClassIds.Register or CosemClassIds.ExtendedRegister or CosemClassIds.DemandRegister) || register.Descriptor.Key.ClassId != classId)
            {
                return new(CosemError.ObjectClassInconsistent, Diagnostic: "Only matching Register, Extended Register, and Demand Register references are permitted.");
            }
        }

        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var mask in masks.Items)
        {
            if (!names.Add(Convert.ToHexString(mask.Items[RegisterStructures.Mask.Name].Bytes.AsSpan())))
            {
                return new(CosemError.TypeUnmatched, Diagnostic: "Mask names must be unique.");
            }

            if (mask.Items[RegisterStructures.Mask.Indices].Items.Any(index => index.AsUInt64() == 0 || index.AsUInt64() > (ulong)assignments.Items.Length))
            {
                return new(CosemError.ScopeOfAccessViolated, Diagnostic: "A mask index must name an existing one-based assignment position.");
            }
        }

        return active != null && FindMask(masks, active) < 0
            ? new(CosemError.ObjectUndefined, Diagnostic: "The active mask must exist in mask_list.")
            : OperationResult.Ok();
    }

    private static DlmsValue StoredArray(CosemObject cosemObject, sbyte index) =>
        cosemObject.State.TryGet(index, out var value) ? value : DlmsValue.Array();

    private static DlmsValue? ActiveMask(CosemObject cosemObject) =>
        cosemObject.State.TryGet(RegisterActivationMembers.Attributes.ActiveMask, out var value) ? value : null;

    private static LogicalName Name(DlmsValue assignment) => LogicalName.FromBytes(assignment.Items[RegisterStructures.Assignment.LogicalName].Bytes.AsSpan());

    private static bool SameName(DlmsValue left, DlmsValue right) => left.Bytes.AsSpan().SequenceEqual(right.Bytes.AsSpan());

    private static int FindMask(DlmsValue masks, DlmsValue name)
    {
        for (int index = 0; index < masks.Items.Length; index++)
        {
            if (SameName(masks.Items[index].Items[RegisterStructures.Mask.Name], name))
            {
                return index;
            }
        }

        return -1;
    }
}
