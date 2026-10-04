using App.Application.Auth;
using App.Application.Dashboard;

namespace App.Api.Endpoints;

public static class DashboardEndpoints
{
    public static IEndpointRouteBuilder MapDashboardEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/admin/dashboard", (DashboardQueries q, CancellationToken ct) => q.GetAsync(ct))
            .RequireAuthorization(Policies.Reviewer).WithTags("Dashboard");
        return app;
    }
}
