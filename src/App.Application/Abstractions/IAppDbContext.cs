using App.Domain.Audit;
using App.Domain.Content;
using App.Domain.Reference;
using App.Domain.Sources;
using Microsoft.EntityFrameworkCore;

namespace App.Application.Abstractions;

public interface IAppDbContext
{
    DbSet<City> Cities { get; }
    DbSet<Category> Categories { get; }
    DbSet<AuditLog> AuditLogs { get; }
    DbSet<Source> Sources { get; }
    DbSet<Candidate> Candidates { get; }
    DbSet<Event> Events { get; }
    DbSet<Deal> Deals { get; }
    DbSet<Venue> Venues { get; }
    DbSet<ItemSource> ItemSources { get; }
    DbSet<JobExecution> JobExecutions { get; }

    Task<int> SaveChangesAsync(CancellationToken ct = default);
}
