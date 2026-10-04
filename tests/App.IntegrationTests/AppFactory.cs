using App.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Testcontainers.PostgreSql;

namespace App.IntegrationTests;

/// <summary>
/// Boots the real API (environment "Testing") against a real PostgreSQL:
/// a Testcontainers instance, or TEST_DB_CONNECTION when set.
/// </summary>
public sealed class AppFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly string? _external = Environment.GetEnvironmentVariable("TEST_DB_CONNECTION");
    private PostgreSqlContainer? _db;

    // https so Secure auth cookies round-trip in tests exactly as in production.
    public AppFactory() => ClientOptions.BaseAddress = new Uri("https://localhost");

    public string ConnectionString => _external ?? _db?.GetConnectionString()
        ?? throw new InvalidOperationException("Database not started.");

    public async Task InitializeAsync()
    {
        if (string.IsNullOrEmpty(_external))
        {
            _db = new PostgreSqlBuilder("postgres:17-alpine").Build();
            await _db.StartAsync();
        }
        // Read by Program.cs during startup (before WebApplicationFactory overrides apply).
        Environment.SetEnvironmentVariable("ConnectionStrings__Default", ConnectionString);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Default", ConnectionString);
        // The real-clock expiration job would flip tests that seed fixed dates; tests call ExpirationRunner directly.
        builder.ConfigureServices(services =>
            services.Remove(services.Single(d => d.ImplementationType == typeof(ExpirationJob))));
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await base.DisposeAsync();
        if (_db is not null) await _db.DisposeAsync();
    }
}
