using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Atea.UnifiedWorkplace.Api.Infrastructure.Persistence;

public sealed class WorkplaceDbContextFactory : IDesignTimeDbContextFactory<WorkplaceDbContext>
{
    public WorkplaceDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<WorkplaceDbContext>()
            .UseNpgsql("Host=localhost;Port=5432;Database=workplace;Username=workplace;Password=workplace")
            .Options;
        return new WorkplaceDbContext(options);
    }
}
