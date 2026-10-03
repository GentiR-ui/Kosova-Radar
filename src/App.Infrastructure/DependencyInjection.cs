using App.Application.Abstractions;
using App.Infrastructure.Identity;
using App.Infrastructure.Persistence;
using App.Infrastructure.Time;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace App.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration config)
    {
        services.AddSingleton<IClock, AppClock>();
        var connectionString = config.GetConnectionString("Default");
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException("ConnectionStrings__Default is not set.");
        services.AddDbContext<AppDbContext>(o => o.UseNpgsql(connectionString));
        services.AddDataProtection().SetApplicationName("KosovaRadar").PersistKeysToDbContext<AppDbContext>();

        services.AddIdentityCore<AppUser>(o =>
            {
                o.Password.RequiredLength = 12;
                o.User.RequireUniqueEmail = true;
            })
            .AddRoles<IdentityRole<Guid>>()
            .AddEntityFrameworkStores<AppDbContext>()
            .AddSignInManager<AppSignInManager>();

        // Deactivation updates the security stamp; validating it on every request ends sessions at once (REQ-003).
        services.Configure<SecurityStampValidatorOptions>(o => o.ValidationInterval = TimeSpan.Zero);

        services.AddMemoryCache();
        services.AddSingleton<ITicketStore, MemoryTicketStore>();
        services.AddOptions<CookieAuthenticationOptions>(IdentityConstants.ApplicationScheme)
            .Configure<ITicketStore>((o, store) => o.SessionStore = store);

        services.AddScoped<BootstrapAdmin>();
        return services;
    }
}
