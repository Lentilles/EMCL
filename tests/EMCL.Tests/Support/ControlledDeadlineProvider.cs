namespace EMCL.Tests.Support;

internal sealed class ControlledDeadlineProvider : TimeProvider
{
    private readonly List<ControlledTimer> timers = [];

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new ControlledTimer(callback, state);
        lock (timers)
        {
            timers.Add(timer);
        }

        return timer;
    }

    internal void ExpireDeadlines()
    {
        ControlledTimer[] snapshot;
        lock (timers)
        {
            snapshot = timers.ToArray();
        }

        foreach (var timer in snapshot)
        {
            timer.Fire();
        }
    }

    private sealed class ControlledTimer(TimerCallback callback, object? state) : ITimer
    {
        private int disposed;
        internal void Fire()
        {
            if (Volatile.Read(ref disposed) == 0)
            {
                callback(state);
            }
        }

        public bool Change(TimeSpan dueTime, TimeSpan period) => Volatile.Read(ref disposed) == 0;
        public void Dispose() => Interlocked.Exchange(ref disposed, 1);
        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
