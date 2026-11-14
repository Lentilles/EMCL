using System.Collections.Concurrent;
using EMCL.Core.Objects;
using EMCL.Core.Operations;
using EMCL.Core.Pipeline;
using EMCL.Core.Values;
using EMCL.Objects.Catalog;
using EMCL.Objects.ProfileGeneric;
using EMCL.Objects.Processing;

namespace EMCL.Objects.Spodes;

/// <summary>Evaluates scheduled actions and monitored values for a local SPODES logical device.</summary>
/// <remarks>Use a single instance per logical device and await calls sequentially. The application supplies meter-local time and measurement events.</remarks>
public sealed class SpodesAutomation
{
    private readonly ObjectCatalog catalog;
    private readonly OperationDelegate execute;
    private readonly HandlerRegistry? handlers;
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly ConcurrentDictionary<CosemObject, MonitorState> monitors = new();
    private readonly Dictionary<CosemObject, DateTimeOffset> schedules = [];

    /// <summary>Creates an automation service using the device's normal operation pipeline.</summary>
    /// <param name="catalog">The SPODES logical device catalog.</param>
    /// <param name="execute">The device operation pipeline.</param>
    /// <param name="handlers">The device registry used to process queued Push operations.</param>
    public SpodesAutomation(ObjectCatalog catalog, OperationDelegate execute, HandlerRegistry? handlers = null)
    {
        this.catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        this.execute = execute ?? throw new ArgumentNullException(nameof(execute));
        this.handlers = handlers;
    }

    /// <summary>Evaluates schedule entries due in the supplied local second and activates due passive calendars.</summary>
    /// <param name="now">The current meter-local time and offset.</param>
    /// <param name="context">The context under which to execute actions.</param>
    /// <param name="cancellationToken">A token that cancels the evaluation.</param>
    public async ValueTask<IReadOnlyList<OperationResult>> TickAsync(DateTimeOffset now, OperationContext context,
        CancellationToken cancellationToken = default)
    {
        RequireDevice(context);
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var results = new List<OperationResult>();
            var objects = catalog.Objects.OrderBy(obj => obj.LogicalName.ToString(), StringComparer.Ordinal).ToArray();
            foreach (var obj in objects)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (obj.Descriptor.Key.ClassId == CosemClassIds.PushSetup
                    && handlers?.Resolve(catalog.LogicalDeviceId, obj) is SpodesPushHandler push)
                {
                    results.Add(await push.ProcessAsync(obj, cancellationToken).ConfigureAwait(false));
                }

                if (obj.Descriptor.Key.ClassId == CosemClassIds.DisconnectControl
                    && handlers?.Resolve(catalog.LogicalDeviceId, obj) is SpodesRelayHandler relay)
                {
                    await relay.CheckpointAsync(obj, cancellationToken).ConfigureAwait(false);
                }

                if (obj.Descriptor.Key.ClassId is not (CosemClassIds.Schedule or CosemClassIds.SingleActionSchedule or CosemClassIds.ActivityCalendar))
                {
                    continue;
                }

                var second = now.AddTicks(-(now.Ticks % TimeSpan.TicksPerSecond));
                if (schedules.TryGetValue(obj, out var previous) && previous >= second)
                {
                    continue;
                }

                schedules[obj] = second;
                if (obj.Descriptor.Key.ClassId == CosemClassIds.ActivityCalendar
                    && obj.State.TryGet(SpodesCalendarHandler.ActivationTime, out var activation)
                    && MatchesDateTime(activation, second))
                {
                    results.Add(await execute(new(CosemOperationKind.Invoke, obj.Descriptor.Key.ClassId,
                        obj.LogicalName, SpodesCalendarHandler.Activate, DlmsValue.Number(DlmsType.Int8, 0)),
                        context, cancellationToken).ConfigureAwait(false));
                }

                foreach (var script in DueScripts(obj, objects, second, previous))
                {
                    results.Add(await SpodesScriptHandler.ExecuteReferenceAsync(script, execute, context,
                        cancellationToken).ConfigureAwait(false));
                }
            }

            return results;
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>Processes a measurement and executes scripts for register threshold crossings and limiter transitions.</summary>
    /// <param name="source">The measured object's logical name.</param>
    /// <param name="attribute">The measured attribute index.</param>
    /// <param name="value">The measured value, retaining its DLMS type.</param>
    /// <param name="now">The timestamp of the measurement.</param>
    /// <param name="context">The context under which to execute actions.</param>
    /// <param name="emergencyGroup">The active emergency profile group, or null when absent.</param>
    /// <param name="cancellationToken">A token that cancels the evaluation.</param>
    public async ValueTask<IReadOnlyList<OperationResult>> ObserveAsync(LogicalName source, sbyte attribute,
        DlmsValue value, DateTimeOffset now, OperationContext context, ushort? emergencyGroup = null,
        CancellationToken cancellationToken = default)
    {
        RequireDevice(context);
        ArgumentNullException.ThrowIfNull(value);
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var results = new List<OperationResult>();
            var measured = catalog.Find(source);
            if (measured is null)
            {
                return [new(CosemResultCode.ObjectUndefined)];
            }

            foreach (var obj in catalog.Objects.Where(obj => obj.Descriptor.Key.ClassId is CosemClassIds.RegisterMonitor or CosemClassIds.Limiter))
            {
                cancellationToken.ThrowIfCancellationRequested();
                bool limiter = obj.Descriptor.Key.ClassId == CosemClassIds.Limiter;
                if (!obj.State.TryGet(limiter ? (sbyte)2 : (sbyte)3, out var monitored)
                    || !ReferenceMatches(monitored, measured, attribute))
                {
                    continue;
                }

                var state = monitors.GetOrAdd(obj, _ => new());
                if (state.Previous is not null && now < state.LastSeen)
                {
                    results.Add(new(CosemResultCode.ScopeOfAccessViolated));
                    continue;
                }

                try
                {
                    var scripts = limiter ? Limit(obj, state, value, now, emergencyGroup)
                        : Crossings(obj, state.Previous, value).ToArray();
                    state.Previous = value;
                    state.LastSeen = now;
                    foreach (var script in scripts)
                    {
                        results.Add(await SpodesScriptHandler.ExecuteReferenceAsync(script, execute, context,
                            cancellationToken).ConfigureAwait(false));
                    }
                }
                catch (Exception error) when (error is ArgumentException or FormatException or InvalidOperationException or OverflowException)
                {
                    results.Add(new(CosemResultCode.TypeUnmatched));
                }
            }

            return results;
        }
        finally
        {
            gate.Release();
        }
    }

    private static IEnumerable<DlmsValue> DueScripts(CosemObject obj, CosemObject[] objects,
        DateTimeOffset now, DateTimeOffset previous)
    {
        switch (obj.Descriptor.Key.ClassId)
        {
            case CosemClassIds.SingleActionSchedule:
                if (obj.State.TryGet(2, out var script) && obj.State.TryGet(4, out var times)
                    && times.Items.Any(entry => MatchesTime(entry.Items[0].Bytes.AsSpan(), now)
                        && MatchesDate(entry.Items[1].Bytes.AsSpan(), now)))
                {
                    yield return script;
                }

                break;
            case CosemClassIds.Schedule:
                if (!obj.State.TryGet(2, out var entries))
                {
                    break;
                }

                int? special = SpecialDay(objects, now);
                int weekday = ((int)now.DayOfWeek + 6) % 7;
                foreach (var entry in entries.Items)
                {
                    var fields = entry.Items;
                    if (fields[1].Value is true && ScheduledTime(fields[4], fields[5], now, previous)
                        && DateBoundary(fields[8].Bytes.AsSpan(), now, lower: true)
                        && DateBoundary(fields[9].Bytes.AsSpan(), now, lower: false)
                        && SpodesArbitratorHandler.HasBit((DlmsBitString)fields[special is null ? 6 : 7].Value!, special is null ? weekday : special.Value - 1))
                    {
                        yield return DlmsValue.Structure(fields[2], fields[3]);
                    }
                }

                break;
            case CosemClassIds.ActivityCalendar:
                foreach (var action in CalendarActions(obj, objects, now))
                {
                    yield return action;
                }

                break;
        }
    }

    private static IEnumerable<DlmsValue> CalendarActions(CosemObject obj, CosemObject[] objects, DateTimeOffset now)
    {
        if (!obj.State.TryGet(3, out var seasons) || !obj.State.TryGet(4, out var weeks) || !obj.State.TryGet(5, out var days))
        {
            yield break;
        }

        int? day = SpecialDay(objects, now);
        if (day is null)
        {
            var season = seasons.Items.Select(value => (Entry: value, Start: SeasonStart(value.Items[1].Bytes.AsSpan(), now)))
                .Where(candidate => candidate.Start <= now).OrderByDescending(candidate => candidate.Start).FirstOrDefault().Entry;
            if (season is null)
            {
                yield break;
            }

            var week = weeks.Items.FirstOrDefault(item => item.Items[0].Bytes.AsSpan().SequenceEqual(season.Items[2].Bytes.AsSpan()));
            if (week is null)
            {
                yield break;
            }

            day = (int)week.Items[((int)now.DayOfWeek + 6) % 7 + 1].AsUInt64();
        }

        var selected = days.Items.FirstOrDefault(item => item.Items[0].AsUInt64() == (ulong)day.Value);
        if (selected is not null)
        {
            foreach (var action in selected.Items[1].Items)
            {
                if (MatchesTime(action.Items[0].Bytes.AsSpan(), now))
                {
                    yield return DlmsValue.Structure(action.Items[1], action.Items[2]);
                }
            }
        }
    }

    private static int? SpecialDay(CosemObject[] objects, DateTimeOffset now)
    {
        foreach (var obj in objects.Where(obj => obj.Descriptor.Key.ClassId == CosemClassIds.SpecialDaysTable))
        {
            if (obj.State.TryGet(2, out var entries))
            {
                var day = entries.Items.FirstOrDefault(item => MatchesDate(item.Items[1].Bytes.AsSpan(), now));
                if (day is not null)
                {
                    return (int)day.Items[2].AsUInt64();
                }
            }
        }

        return null;
    }

    private static bool ScheduledTime(DlmsValue time, DlmsValue validity, DateTimeOffset now, DateTimeOffset previous)
    {
        if (MatchesTime(time.Bytes.AsSpan(), now))
        {
            return true;
        }

        var fields = time.Bytes;
        if (fields.Length != 4 || fields[0] > 23 || fields[1] > 59 || fields[2] > 59)
        {
            return false;
        }

        var due = new DateTimeOffset(now.Year, now.Month, now.Day, fields[0], fields[1], fields[2], now.Offset);
        return due > previous && due < now && now - due <= TimeSpan.FromMinutes(validity.AsInt64());
    }

    private static bool DateBoundary(ReadOnlySpan<byte> fields, DateTimeOffset now, bool lower)
    {
        var date = ConcreteDate(fields, now.Year, now.Month, lower ? 1 : DateTime.DaysInMonth(now.Year, now.Month));
        return date is null || (lower ? now.Date >= date : now.Date <= date);
    }

    private static DateTimeOffset SeasonStart(ReadOnlySpan<byte> fields, DateTimeOffset now)
    {
        if (fields.Length != 12 || fields[..9].IndexOfAnyExcept((byte)0xff) < 0)
        {
            return DateTimeOffset.MaxValue;
        }

        bool recurring = fields[0] == 255 && fields[1] == 255;
        var date = ConcreteDate(fields[..5], now.Year, now.Month, 1);
        if (recurring && date > now.Date && now.Year > 1)
        {
            date = ConcreteDate(fields[..5], now.Year - 1, now.Month, 1);
        }

        return date is null ? DateTimeOffset.MaxValue : new(date.Value, now.Offset);
    }

    private static DateTime? ConcreteDate(ReadOnlySpan<byte> fields, int defaultYear, int defaultMonth, int defaultDay)
    {
        if (fields.Length != 5)
        {
            return null;
        }

        int year = fields[0] == 255 && fields[1] == 255 ? defaultYear : (fields[0] << 8) | fields[1];
        int month = fields[2] == 255 ? defaultMonth : fields[2];
        if (year is < 1 or > 9999 || month is < 1 or > 12)
        {
            return null;
        }

        int last = DateTime.DaysInMonth(year, month);
        int day = fields[3] switch
        {
            255 => Math.Min(defaultDay, last),
            254 => last,
            253 => last - 1,
            _ => fields[3]
        };
        return day < 1 || day > last ? null : new DateTime(year, month, day);
    }

    internal static bool MatchesDateTime(DlmsValue value, DateTimeOffset time)
        => value.Bytes.Length == 12 && value.Bytes.AsSpan()[..9].IndexOfAnyExcept((byte)0xff) >= 0
            && MatchesDate(value.Bytes.AsSpan()[..5], time)
            && MatchesTime(value.Bytes.AsSpan().Slice(5, 4), time);

    private static bool MatchesDate(ReadOnlySpan<byte> fields, DateTimeOffset time)
    {
        int weekday = time.DayOfWeek == DayOfWeek.Sunday ? 7 : (int)time.DayOfWeek;
        return fields.Length == 5 && ConcreteDate(fields, time.Year, time.Month, time.Day) == time.Date
            && (fields[4] == 255 || fields[4] == weekday);
    }

    private static bool MatchesTime(ReadOnlySpan<byte> fields, DateTimeOffset time)
        => fields.Length == 4 && (fields[0] == 255 || fields[0] == time.Hour)
            && (fields[1] == 255 || fields[1] == time.Minute) && (fields[2] == 255 || fields[2] == time.Second)
            && (fields[3] == 255 || fields[3] == time.Millisecond / 10);

    private static bool ReferenceMatches(DlmsValue reference, CosemObject measured, sbyte attribute)
    {
        var fields = reference.Items;
        return fields[0].AsUInt64() == measured.Descriptor.Key.ClassId
            && fields[1].Bytes.AsSpan().SequenceEqual(measured.LogicalName.ToBytes())
            && fields[2].AsInt64() == attribute;
    }

    private static IEnumerable<DlmsValue> Crossings(CosemObject obj, DlmsValue? previous, DlmsValue current)
    {
        if (previous is null || !obj.State.TryGet(2, out var thresholds) || !obj.State.TryGet(4, out var actions))
        {
            yield break;
        }

        if (thresholds.Items.Length != actions.Items.Length || thresholds.Items.Any(threshold => threshold.Type != current.Type))
        {
            throw new InvalidOperationException("Monitor thresholds and actions must match.");
        }

        for (int i = 0; i < thresholds.Items.Length; i++)
        {
            var threshold = thresholds.Items[i];
            bool before = ValueComparison.Compare(previous, threshold) >= 0;
            bool after = ValueComparison.Compare(current, threshold) >= 0;
            if (before != after)
            {
                yield return actions.Items[i].Items[after ? 0 : 1];
            }
        }
    }

    private static DlmsValue[] Limit(CosemObject obj, MonitorState state, DlmsValue current,
        DateTimeOffset now, ushort? emergencyGroup)
    {
        bool emergency = false;
        if (emergencyGroup is not null && obj.State.TryGet(8, out var profile) && obj.State.TryGet(9, out var groups)
            && groups.Items.Any(id => id.AsUInt64() == emergencyGroup))
        {
            var start = CosemDateTime.FromValue(profile.Items[1]).ToDateTimeOffset();
            emergency = now >= start && now - start < TimeSpan.FromSeconds(profile.Items[2].AsInt64());
        }

        if (!obj.State.TryGet(emergency ? (sbyte)5 : (sbyte)4, out var threshold)
            || !obj.State.TryGet(11, out var actions))
        {
            return [];
        }

        if (current.Type != threshold.Type)
        {
            throw new InvalidOperationException("Limiter thresholds must have the monitored value's wire type.");
        }

        obj.State.Set(3, threshold);
        obj.State.Set(10, new(DlmsType.Boolean, emergency));
        bool above = ValueComparison.Compare(current, threshold) >= 0;
        if (state.Previous is null || state.Above != above || state.Threshold is not { } old
            || ValueComparison.Compare(old, threshold) != 0)
        {
            state.Above = above;
            state.Since = now;
            state.Threshold = threshold;
        }

        var duration = obj.State.TryGet(above ? (sbyte)6 : (sbyte)7, out var configured) ? configured.AsInt64() : 0;
        if (above != state.AppliedAbove && now - state.Since >= TimeSpan.FromSeconds(duration))
        {
            state.AppliedAbove = above;
            return [actions.Items[above ? 0 : 1]];
        }

        return [];
    }

    private void RequireDevice(OperationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.LogicalDeviceId != catalog.LogicalDeviceId)
        {
            throw new ArgumentException("The operation context belongs to a different logical device.", nameof(context));
        }
    }

    private sealed class MonitorState
    {
        internal DlmsValue? Previous
        {
            get; set;
        }
        internal DlmsValue? Threshold
        {
            get; set;
        }
        internal DateTimeOffset LastSeen
        {
            get; set;
        }
        internal DateTimeOffset Since
        {
            get; set;
        }
        internal bool Above
        {
            get; set;
        }
        internal bool AppliedAbove
        {
            get; set;
        }
    }
}
