using App.Domain.Reference;
using Microsoft.EntityFrameworkCore;

namespace App.Application.Abstractions;

public interface IAppDbContext
{
    DbSet<City> Cities { get; }
    DbSet<Category> Categories { get; }

    Task<int> SaveChangesAsync(CancellationToken ct = default);
}
