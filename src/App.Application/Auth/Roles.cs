namespace App.Application.Auth;

public static class Roles
{
    public const string Admin = "Admin";
    public const string Reviewer = "Reviewer";

    public static readonly IReadOnlyList<string> All = [Admin, Reviewer];

    /// <summary>Wire format: camelCase ("admin", "reviewer").</summary>
    public static string ToWire(string role) => role.ToLowerInvariant();
}

public static class Policies
{
    /// <summary>Admin only: sources and users.</summary>
    public const string Admin = "Admin";

    /// <summary>Admin or Reviewer: dashboard, candidates, published items, audit.</summary>
    public const string Reviewer = "Reviewer";
}
