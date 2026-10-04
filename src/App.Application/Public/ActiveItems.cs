using System.Linq.Expressions;
using App.Domain.Content;

namespace App.Application.Public;

/// <summary>SQL-translatable twins of <c>ContentRules.IsActiveEvent</c> (kept equal by an integration test).</summary>
public static class ActiveItems
{
    public static Expression<Func<Event, bool>> Event(DateTimeOffset now)
    {
        var noEndCutoff = now - ContentRules.DefaultEventDuration;
        return e => e.Status == ItemStatus.Published
            && (e.EndsAt != null && e.EndsAt >= e.StartsAt ? e.EndsAt > now : e.StartsAt > noEndCutoff);
    }
}
