namespace App.Api;

/// <summary>Startup checks that stop a production host from running with development defaults (T043).</summary>
public static class ProductionGuards
{
    // The documented credentials in appsettings.Development.json; they must never reach a real deployment.
    private const string DevAdminEmail = "admin@kosovaradar.local";
    private const string DevAdminPassword = "Radar-admin-dev-2026";

    public static void RejectDevBootstrapCredentials(IConfiguration config, IHostEnvironment env)
    {
        if (env.IsDevelopment() || env.IsEnvironment("Testing")) return;
        if (string.Equals(config["Bootstrap:AdminPassword"], DevAdminPassword, StringComparison.Ordinal)
            || string.Equals(config["Bootstrap:AdminEmail"]?.Trim(), DevAdminEmail, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                $"The development bootstrap admin credentials must not be used in {env.EnvironmentName}. Set Bootstrap__AdminEmail and Bootstrap__AdminPassword to your own values.");
    }

    // Without a trusted proxy every visitor shares the proxy's IP: one attacker could lock all admins out
    // (login throttle), the public rate limit becomes one global bucket and HSTS is never sent.
    public static void RequireTrustedProxy(IConfiguration config, IHostEnvironment env)
    {
        if (env.IsDevelopment() || env.IsEnvironment("Testing")) return;
        if (string.IsNullOrWhiteSpace(config["Proxy:KnownProxies"]) && string.IsNullOrWhiteSpace(config["Proxy:KnownNetworks"]))
            throw new InvalidOperationException(
                $"Proxy__KnownProxies or Proxy__KnownNetworks must be set in {env.EnvironmentName} (the reverse proxy whose X-Forwarded-* headers are trusted).");
    }
}
