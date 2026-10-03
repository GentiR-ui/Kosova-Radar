using App.Infrastructure;
using App.Infrastructure.Identity;
using App.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace App.IntegrationTests;

[Collection(IntegrationCollection.Name)]
public class BootstrapAdminTests(AppFactory app)
{
    /// <summary>Services over a brand-new empty database on the same server.</summary>
    private ServiceProvider FreshDatabase(string? email, string? password)
    {
        var cs = new NpgsqlConnectionStringBuilder(app.ConnectionString) { Database = $"bootstrap_{Guid.NewGuid():N}" };
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Default"] = cs.ConnectionString,
            ["Bootstrap:AdminEmail"] = email,
            ["Bootstrap:AdminPassword"] = password,
        }).Build();
        var services = new ServiceCollection().AddLogging().AddSingleton<IConfiguration>(config);
        services.AddInfrastructure(config);
        return services.BuildServiceProvider();
    }

    private static async Task<(int Users, BootstrapAdmin Bootstrap)> MigrateAsync(IServiceScope scope)
    {
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync();
        return (await db.Users.CountAsync(), scope.ServiceProvider.GetRequiredService<BootstrapAdmin>());
    }

    [Fact]
    public async Task Creates_the_admin_only_when_no_user_exists()
    {
        await using var sp = FreshDatabase("first@test.local", "First-admin-pass-1");
        await using (var scope = sp.CreateAsyncScope())
        {
            var (count, bootstrap) = await MigrateAsync(scope);
            Assert.Equal(0, count);
            await bootstrap.EnsureAsync();
            await bootstrap.EnsureAsync(); // idempotent
        }

        await using (var scope = sp.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var user = Assert.Single(await db.Users.ToListAsync());
            Assert.Equal("first@test.local", user.Email);
            Assert.True(user.IsActive);
            var role = await (from ur in db.UserRoles join r in db.Roles on ur.RoleId equals r.Id
                              where ur.UserId == user.Id select r.Name).SingleAsync();
            Assert.Equal("Admin", role);
            await db.Database.EnsureDeletedAsync();
        }
    }

    [Fact]
    public async Task Fails_clearly_when_no_user_exists_and_no_bootstrap_config_is_set()
    {
        await using var sp = FreshDatabase(null, null);
        await using var scope = sp.CreateAsyncScope();
        var (_, bootstrap) = await MigrateAsync(scope);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => bootstrap.EnsureAsync());
        Assert.Contains("Bootstrap__AdminEmail", ex.Message);
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.EnsureDeletedAsync();
    }
}
