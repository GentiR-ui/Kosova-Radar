using App.Domain.Audit;
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

    Task<int> SaveChangesAsync(CancellationToken ct = default);
}
