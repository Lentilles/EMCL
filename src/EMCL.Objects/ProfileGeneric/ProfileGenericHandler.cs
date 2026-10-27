using System.Collections.Immutable;
using EMCL.Core.Codecs;
using EMCL.Core.Objects;
using EMCL.Core.Operations;
using EMCL.Core.Values;
using EMCL.Objects.Catalog;
using EMCL.Objects.Definitions;
using EMCL.Objects.Handlers;
using EMCL.Objects.Internal;

namespace EMCL.Objects.ProfileGeneric;

/// <summary>Executes Profile Generic capture, reset, sorting, capacity changes, and selective buffer reads.</summary>
/// <remarks>Periodic capture must be scheduled by the application. Capturing multiple source objects is not a transactional snapshot unless the injected capture source provides that guarantee.</remarks>
/// <param name="catalog">The logical-device catalog.</param>
/// <param name="captures">The source used to read each captured attribute value.</param>
/// <param name="protocolLimits">Optional resource bounds; null selects validated defaults.</param>
public sealed class ProfileGenericHandler(ObjectCatalog catalog, ICaptureValueSource captures, ProtocolLimits? protocolLimits = null) : StoredValueHandler
{
    private readonly ProtocolLimits limits = ProtocolLimits.Resolve(protocolLimits);
    private static DlmsValue Number(uint n) => new(DlmsType.UInt32, n);
    private static void Defaults(CosemObject obj)
    {
        void Default(sbyte index, DlmsValue value)
        {
            if (!obj.State.TryGet(index, out _))
            {
                obj.State.Set(index, value);
            }
        }

        Default(ProfileGenericMembers.Attributes.Buffer, DlmsValue.Array());
        Default(ProfileGenericMembers.Attributes.CaptureObjects, DlmsValue.Array());
        Default(ProfileGenericMembers.Attributes.CapturePeriod, Number(0));
        Default(ProfileGenericMembers.Attributes.SortMethod, DlmsValue.Number(DlmsType.Enum, (byte)ProfileSortMethod.Fifo));
        Default(ProfileGenericMembers.Attributes.EntriesInUse, Number(0));
        Default(ProfileGenericMembers.Attributes.ProfileEntries, Number(1));
    }

    /// <summary>Reads the row collection from either an array or compact-array profile buffer.</summary>
    /// <param name="buffer">The read/write byte buffer or Profile Generic buffer value.</param>
    internal static ImmutableArray<DlmsValue> GetRows(DlmsValue buffer) => buffer.Type switch
    {
        DlmsType.Array => buffer.Items,
        DlmsType.CompactArray => ((DlmsCompactArray)buffer.Value!).Rows,
        _ => throw new ArgumentException("Invalid profile buffer.")
    };
    private static DlmsValue Get(CosemObject obj, sbyte index) => obj.State.TryGet(index, out var value) ? value : throw new InvalidOperationException($"Attribute {index} is unavailable.");
    private static CaptureObject[] Definitions(CosemObject obj) => Schema.Collection(Get(obj, ProfileGenericMembers.Attributes.CaptureObjects), DlmsType.Array).Select(CaptureObject.Parse).ToArray();
    private static void SetRows(CosemObject obj, IEnumerable<DlmsValue> rows)
    {
        var array = rows.ToArray();
        obj.State.Set(ProfileGenericMembers.Attributes.Buffer, DlmsValue.Array(array));
        obj.State.Set(ProfileGenericMembers.Attributes.EntriesInUse, Number((uint)array.Length));
    }

    /// <inheritdoc />
    public override ValueTask<OperationResult> ReadAsync(CosemObject cosemObject, CosemOperation operation, OperationContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Defaults(cosemObject);
        if (operation.Index != ProfileGenericMembers.Attributes.Buffer || operation.Selection == null)
        {
            return base.ReadAsync(cosemObject, operation, context, cancellationToken);
        }

        var rows = GetRows(Get(cosemObject, ProfileGenericMembers.Attributes.Buffer));
        var definitions = Definitions(cosemObject);
        return ValueTask.FromResult(OperationResult.Ok(operation.Selection.Selector switch
        {
            ProfileSelectors.Range => SelectRange(rows, definitions, operation.Selection.Parameters),
            ProfileSelectors.Entry => SelectEntries(rows, definitions.Length, operation.Selection.Parameters),
            _ => throw new ArgumentException("Unsupported profile selector.")
        }));
    }

    private static DlmsValue SelectEntries(ImmutableArray<DlmsValue> rows, int width, DlmsValue parameters)
    {
        var p = Schema.Structure(parameters, ProfileStructures.EntrySelection.Count);
        uint first = checked((uint)Schema.Number(p[ProfileStructures.EntrySelection.FirstEntry], DlmsType.UInt32)), last = checked((uint)Schema.Number(p[ProfileStructures.EntrySelection.LastEntry], DlmsType.UInt32));
        ushort fromColumn = checked((ushort)Schema.Number(p[ProfileStructures.EntrySelection.FirstColumn], DlmsType.UInt16)), toColumn = checked((ushort)Schema.Number(p[ProfileStructures.EntrySelection.LastColumn], DlmsType.UInt16));
        if (first == 0 || fromColumn == 0)
        {
            throw new ArgumentException("Profile entry and column indices start at one.");
        }

        if (last == 0)
        {
            last = (uint)rows.Length;
        }

        if (toColumn == 0)
        {
            toColumn = checked((ushort)width);
        }

        if (fromColumn > width || toColumn > width || fromColumn > toColumn)
        {
            throw new ArgumentException("Invalid column interval.");
        }

        if (first > last)
        {
            return DlmsValue.Array();
        }

        return DlmsValue.Array(rows.Where((_, i) => (uint)i + 1 >= first && (uint)i + 1 <= last).Select(row => DlmsValue.Structure(row.Items.Skip(fromColumn - 1).Take(toColumn - fromColumn + 1).ToArray())).ToArray());
    }

    private static DlmsValue SelectRange(ImmutableArray<DlmsValue> rows, CaptureObject[] definitions, DlmsValue parameters)
    {
        var p = Schema.Structure(parameters, ProfileStructures.RangeSelection.Count);
        var restricting = CaptureObject.Parse(p[ProfileStructures.RangeSelection.RestrictingObject]);
        int column = Array.IndexOf(definitions, restricting);
        if (column < 0 || ValueComparison.Compare(p[ProfileStructures.RangeSelection.FromValue], p[ProfileStructures.RangeSelection.ToValue]) > 0)
        {
            throw new ArgumentException("Invalid range descriptor.");
        }

        var selected = Schema.Collection(p[ProfileStructures.RangeSelection.SelectedValues], DlmsType.Array).Select(CaptureObject.Parse).ToArray();
        int[] columns = selected.Length == 0 ? Enumerable.Range(0, definitions.Length).ToArray() : selected.Select(s => Array.IndexOf(definitions, s)).ToArray();
        if (columns.Any(i => i < 0))
        {
            throw new ArgumentException("Selected capture object is absent.");
        }

        return DlmsValue.Array(rows.Where(row => ValueComparison.Compare(row.Items[column], p[ProfileStructures.RangeSelection.FromValue]) >= 0 && ValueComparison.Compare(row.Items[column], p[ProfileStructures.RangeSelection.ToValue]) <= 0).Select(row => DlmsValue.Structure(columns.Select(i => row.Items[i]).ToArray())).ToArray());
    }

    /// <inheritdoc />
    public override ValueTask<OperationResult> WriteAsync(CosemObject cosemObject, CosemOperation operation, OperationContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Defaults(cosemObject);
        if (operation.Selection != null)
        {
            return ValueTask.FromResult(new OperationResult(CosemError.ScopeOfAccessViolated));
        }

        if (operation.Index == ProfileGenericMembers.Attributes.EntriesInUse)
        {
            return ValueTask.FromResult(new OperationResult(CosemError.ReadWriteDenied));
        }

        if (operation.Index == ProfileGenericMembers.Attributes.CaptureObjects)
        {
            var definitions = Schema.Collection(operation.Value!, DlmsType.Array).Select(CaptureObject.Parse).ToArray();
            if (definitions.Length > limits.MaxCollectionItems)
            {
                throw new ArgumentException("Too many capture objects.");
            }

            cosemObject.State.Set(ProfileGenericMembers.Attributes.CaptureObjects, operation.Value!);
            ClearDependentBuffers(cosemObject.LogicalName);
            return ValueTask.FromResult(OperationResult.Ok());
        }

        if (operation.Index == ProfileGenericMembers.Attributes.SortObject)
        {
            _ = CaptureObject.Parse(operation.Value!);
        }

        if (operation.Index == ProfileGenericMembers.Attributes.ProfileEntries && operation.Value!.AsUInt64() > (ulong)limits.MaxCollectionItems)
        {
            throw new ArgumentException("Profile capacity limit exceeded.");
        }

        if (operation.Index == ProfileGenericMembers.Attributes.Buffer)
        {
            var rows = GetRows(operation.Value!);
            int width = Definitions(cosemObject).Length;
            if (rows.Any(r => r.Type != DlmsType.Structure || r.Items.Length != width))
            {
                throw new ArgumentException("Profile row shape does not match capture_objects.");
            }

            var retained = OrderAndLimit(cosemObject, rows.ToList());
            // Preserve a compact representation when no row was removed or reordered.
            if (operation.Value!.Type == DlmsType.CompactArray && retained.SequenceEqual(rows))
            {
                cosemObject.State.Set(ProfileGenericMembers.Attributes.Buffer, operation.Value);
                cosemObject.State.Set(ProfileGenericMembers.Attributes.EntriesInUse, Number((uint)rows.Length));
            }
            else
            {
                SetRows(cosemObject, retained);
            }

            return ValueTask.FromResult(OperationResult.Ok());
        }

        if (operation.Index is ProfileGenericMembers.Attributes.SortMethod or ProfileGenericMembers.Attributes.SortObject or ProfileGenericMembers.Attributes.ProfileEntries)
        {
            var rows = OrderAndLimit(cosemObject, GetRows(Get(cosemObject, ProfileGenericMembers.Attributes.Buffer)).ToList(), (operation.Index, operation.Value!));
            cosemObject.State.Set(operation.Index, operation.Value!);
            SetRows(cosemObject, rows);
            return ValueTask.FromResult(OperationResult.Ok());
        }

        return base.WriteAsync(cosemObject, operation, context, cancellationToken);
    }

    private void ClearDependentBuffers(LogicalName changed)
    {
        var pending = new Queue<LogicalName>();
        var seen = new HashSet<LogicalName>();
        pending.Enqueue(changed);
        while (pending.TryDequeue(out var name))
        {
            if (!seen.Add(name))
            {
                continue;
            }

            if (catalog.Find(name) is { } target)
            {
                SetRows(target, []);
            }

            foreach (var profile in catalog.Objects.Where(o => o.Descriptor.Key.ClassId == CosemClassIds.ProfileGeneric))
            {
                if (profile.State.TryGet(ProfileGenericMembers.Attributes.CaptureObjects, out var defs) && defs.Items.Select(CaptureObject.Parse).Any(c => c.LogicalName == name && c.AttributeIndex == ProfileGenericMembers.Attributes.Buffer))
                {
                    pending.Enqueue(profile.LogicalName);
                }
            }
        }
    }

    /// <inheritdoc />
    public override async ValueTask<OperationResult> InvokeAsync(CosemObject cosemObject, CosemOperation operation, OperationContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Defaults(cosemObject);
        if (operation.Value?.Type != DlmsType.Int8 || operation.Value.AsInt64() != 0)
        {
            return new(CosemError.TypeUnmatched);
        }

        if (operation.Index == ProfileGenericMembers.Methods.Reset)
        {
            SetRows(cosemObject, []);
            return OperationResult.Ok();
        }

        if (operation.Index != ProfileGenericMembers.Methods.Capture)
        {
            return await base.InvokeAsync(cosemObject, operation, context, cancellationToken).ConfigureAwait(false);
        }

        var cells = new List<DlmsValue>();
        foreach (var capture in Definitions(cosemObject))
        {
            var result = await captures.ReadAsync(capture, context, cancellationToken).ConfigureAwait(false);
            if (result.Error != CosemError.Success)
            {
                return result;
            }

            cells.Add(result.Value!);
        }

        var rows = GetRows(Get(cosemObject, ProfileGenericMembers.Attributes.Buffer)).ToList();
        var entry = DlmsValue.Structure(cells.ToArray());
        if (Get(cosemObject, ProfileGenericMembers.Attributes.SortMethod).AsInt64() == (byte)ProfileSortMethod.Lifo)
        {
            rows.Insert(0, entry);
        }
        else
        {
            rows.Add(entry);
        }

        SetRows(cosemObject, OrderAndLimit(cosemObject, rows));
        return OperationResult.Ok();
    }

    private List<DlmsValue> OrderAndLimit(CosemObject obj, List<DlmsValue> rows, (sbyte Index, DlmsValue Value)? pending = null)
    {
        DlmsValue At(sbyte index) => pending.HasValue && pending.Value.Index == index ? pending.Value.Value : Get(obj, index);
        if (rows.Count > limits.MaxCollectionItems)
        {
            throw new ArgumentException("Profile row limit exceeded.");
        }

        long method = At(ProfileGenericMembers.Attributes.SortMethod).AsInt64();
        int capacity = checked((int)At(ProfileGenericMembers.Attributes.ProfileEntries).AsUInt64());
        if (capacity < 1 || capacity > limits.MaxCollectionItems)
        {
            throw new ArgumentException("Invalid profile capacity.");
        }

        if (method is < (byte)ProfileSortMethod.Fifo or > (byte)ProfileSortMethod.FarthestFromZero)
        {
            throw new ArgumentException("Invalid profile sort method.");
        }

        if (method >= (byte)ProfileSortMethod.Largest && rows.Count > 0)
        {
            int index = Array.IndexOf(Definitions(obj), CaptureObject.Parse(At(ProfileGenericMembers.Attributes.SortObject)));
            if (index < 0)
            {
                throw new ArgumentException("sort_object is not a capture object.");
            }

            int Compare(DlmsValue a, DlmsValue b)
            {
                var x = a.Items[index];
                var y = b.Items[index];
                int comparison = method >= (byte)ProfileSortMethod.NearestToZero ? Math.Abs(ValueComparison.Numeric(x)).CompareTo(Math.Abs(ValueComparison.Numeric(y))) : ValueComparison.Compare(x, y);
                return method is (byte)ProfileSortMethod.Largest or (byte)ProfileSortMethod.FarthestFromZero ? -comparison : comparison;
            }

            // Stable ordering retains the relative capture order for equal sort values.
            rows = rows.OrderBy(r => r, Comparer<DlmsValue>.Create(Compare)).ToList();
        }

        if (rows.Count > capacity)
        {
            rows = method == (byte)ProfileSortMethod.Fifo ? rows.Skip(rows.Count - capacity).ToList() : rows.Take(capacity).ToList();
        }

        return rows;
    }
}
