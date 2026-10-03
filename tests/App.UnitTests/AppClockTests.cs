using App.Infrastructure.Time;

namespace App.UnitTests;

public class AppClockTests
{
    [Fact]
    public async Task Override_applies_to_its_own_async_flow_only()
    {
        var clock = new AppClock();
        var fixedTime = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);

        var other = Task.Run(async () => { await Task.Delay(50); return clock.UtcNow; });
        AppClock.SetOverride(fixedTime);
        try
        {
            await Task.Yield();
            Assert.Equal(fixedTime, clock.UtcNow);
        }
        finally { AppClock.SetOverride(null); }

        var tolerance = TimeSpan.FromSeconds(5);
        Assert.InRange(await other, DateTimeOffset.UtcNow - tolerance, DateTimeOffset.UtcNow + tolerance);
        Assert.InRange(clock.UtcNow, DateTimeOffset.UtcNow - tolerance, DateTimeOffset.UtcNow + tolerance);
    }
}
