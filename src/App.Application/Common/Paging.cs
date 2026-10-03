namespace App.Application.Common;

/// <summary>Paged list on the wire (design.md §5).</summary>
public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount);

public static class Paging
{
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 100;

    /// <summary>Applies defaults (page 1, size 20) and returns field errors for out-of-range values.</summary>
    public static (int Page, int PageSize, Dictionary<string, string[]>? Errors) Parse(int? page, int? pageSize)
    {
        var errors = new Dictionary<string, string[]>();
        if (page is < 1) errors["page"] = ["Must be 1 or greater."];
        if (pageSize is < 1 or > MaxPageSize) errors["pageSize"] = [$"Must be between 1 and {MaxPageSize}."];
        return (page ?? 1, pageSize ?? DefaultPageSize, errors.Count > 0 ? errors : null);
    }
}
