using System.Net;
using Microsoft.AspNetCore.Hosting;

namespace App.IntegrationTests;

[Collection(IntegrationCollection.Name)]
public class TestHooksTests(AppFactory app)
{
    [Fact]
    public async Task Hooks_are_inert_when_disabled()
    {
        await using var disabled = app.WithWebHostBuilder(b => b.UseSetting("TestHooks:Enabled", "false"));
        var client = disabled.CreateClient();

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/_test/throw")).StatusCode);

        var req = new HttpRequestMessage(HttpMethod.Get, "/api/me");
        req.Headers.Add("X-Test-Now", "not a date"); // would be 400 if the hook were active
        Assert.NotEqual(HttpStatusCode.BadRequest, (await client.SendAsync(req)).StatusCode);
    }

    [Fact]
    public async Task Invalid_test_clock_header_is_rejected_when_enabled()
    {
        var req = new HttpRequestMessage(HttpMethod.Get, "/api/me");
        req.Headers.Add("X-Test-Now", "10/03/2026"); // not ISO-8601
        Assert.Equal(HttpStatusCode.BadRequest, (await app.CreateClient().SendAsync(req)).StatusCode);
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public void Startup_fails_when_hooks_are_enabled_outside_development_and_testing(string environment)
    {
        using var prod = app.WithWebHostBuilder(b => b
            .UseEnvironment(environment)
            .UseSetting("TestHooks:Enabled", "true"));
        var ex = Assert.ThrowsAny<Exception>(() => prod.CreateClient());
        Assert.Contains("TestHooks:Enabled", ex.ToString());
    }
}
