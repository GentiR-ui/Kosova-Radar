using System.Net;
using Microsoft.AspNetCore.Hosting;

namespace App.IntegrationTests;

[Collection(IntegrationCollection.Name)]
public class HttpBaselineTests(AppFactory app)
{
    [Fact]
    public async Task Public_api_is_rate_limited_per_client_when_configured()
    {
        await using var limited = app.WithWebHostBuilder(b => b.UseSetting("RateLimit:PublicPerMinute", "3"));
        var client = limited.CreateClient();
        for (var i = 0; i < 3; i++)
            Assert.NotEqual((HttpStatusCode)429, (await client.GetAsync("/api/events")).StatusCode);
        Assert.Equal((HttpStatusCode)429, (await client.GetAsync("/api/events")).StatusCode);

        // Admin and auth routes are not counted.
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/admin/sources")).StatusCode);
    }

    [Fact]
    public async Task Rate_limiting_is_off_in_testing()
    {
        var client = app.CreateClient();
        for (var i = 0; i < 50; i++)
            Assert.NotEqual((HttpStatusCode)429, (await client.GetAsync("/api/events")).StatusCode);
    }

    [Fact]
    public async Task Problem_details_for_unknown_api_routes()
    {
        var res = await app.CreateClient().GetAsync("/api/does-not-exist");
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
        Assert.Equal("application/problem+json", res.Content.Headers.ContentType?.MediaType);
    }
}
