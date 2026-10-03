using App.Application.Abstractions;

namespace App.Infrastructure.Time;

/// <summary>
/// Real UTC clock with an optional per-request override (the X-Test-Now test hook, ADR-014).
/// The override is an AsyncLocal, so it flows through the awaits of the request that set it and never
/// reaches background services, which always see the real clock.
/// </summary>
public sealed class AppClock : IClock
{
    private static readonly AsyncLocal<DateTimeOffset?> Override = new();

    public DateTimeOffset UtcNow => Override.Value ?? DateTimeOffset.UtcNow;

    /// <summary>Sets the override for the current async flow. Only the test-hooks middleware calls this.</summary>
    public static void SetOverride(DateTimeOffset? utcNow) => Override.Value = utcNow?.ToUniversalTime();
}
