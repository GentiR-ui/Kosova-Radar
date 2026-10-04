using App.Application.Abstractions;
using App.Application.Audit;
using App.Application.Candidates;
using App.Application.Ingestion;
using App.Application.Public;
using App.Application.Sources;
using App.Infrastructure.Audit;
using App.Infrastructure.Identity;
using App.Infrastructure.Ingestion;
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
                // REQ-001: the only password rule is the length; complexity rules are off (ADR-022).
                o.Password.RequireDigit = o.Password.RequireLowercase = o.Password.RequireUppercase = o.Password.RequireNonAlphanumeric = false;
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

        services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<AppDbContext>());
        services.AddScoped<IAuditWriter, AuditWriter>();
        services.AddScoped<ReferenceDataSeeder>();
        services.AddScoped<BootstrapAdmin>();
        services.AddScoped<UserAdmin>();
        services.AddSingleton(config.GetSection(IngestionOptions.Section).Get<IngestionOptions>() ?? new IngestionOptions());
        services.AddScoped<SourceAdmin>();
        services.AddScoped<CandidateDecisions>();
        services.AddScoped<CandidateApproval>();
        services.AddScoped<PublicItemQueries>();
        services.AddScoped<PublicEventQueries>();
        services.AddScoped<PublicDealQueries>();
        services.AddScoped<PublicSearchQueries>();
        services.AddScoped<ExpirationRunner>();
        services.AddHostedService<ExpirationJob>();
        services.AddSingleton<SourceRunLock>();
        services.AddScoped<ISourceAdapter, FixtureSourceAdapter>();
        services.AddScoped<IngestionService>();
        return services;
    }
}
