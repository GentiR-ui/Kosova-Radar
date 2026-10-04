using Microsoft.AspNetCore.Hosting;

namespace App.IntegrationTests;

[Collection(IntegrationCollection.Name)]
public class ProductionGuardTests(AppFactory app)
{
    [Theory]
    [InlineData("Bootstrap:AdminPassword", "Radar-admin-dev-2026")]
    [InlineData("Bootstrap:AdminEmail", "admin@kosovaradar.local")]
    public void Production_refuses_the_dev_bootstrap_credentials(string key, string value)
    {
        using var prod = app.WithWebHostBuilder(b => b
            .UseEnvironment("Production")
            .UseSetting("Site:BaseUrl", "https://example.com")
            .UseSetting(key, value));
        var ex = Assert.ThrowsAny<Exception>(() => prod.CreateClient());
        Assert.Contains("development bootstrap admin credentials", ex.ToString());
    }
}
