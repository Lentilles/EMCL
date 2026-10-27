namespace EMCL.Tests.Support;

internal sealed class ManualTimeProvider : TimeProvider
{
    internal DateTimeOffset Now { get; set; } = new(2026, 10, 26, 12, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => Now;
}
