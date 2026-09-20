using Atea.UnifiedWorkplace.Api.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace Atea.UnifiedWorkplace.Api.Infrastructure.Persistence.Repositories;

public interface IUserPreferenceRepository
{
    Task<string?> GetThemeAsync(Guid tenantId, Guid userObjectId, CancellationToken cancellationToken = default);
    Task SetThemeAsync(Guid tenantId, Guid userObjectId, string theme, CancellationToken cancellationToken = default);
}

public sealed class UserPreferenceRepository(WorkplaceDbContext db) : IUserPreferenceRepository
{
    public Task<string?> GetThemeAsync(Guid tenantId, Guid userObjectId, CancellationToken cancellationToken = default) =>
        db.UserPreferences.AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.UserObjectId == userObjectId)
            .Select(x => x.Theme)
            .SingleOrDefaultAsync(cancellationToken);

    public async Task SetThemeAsync(Guid tenantId, Guid userObjectId, string theme, CancellationToken cancellationToken = default)
    {
        var preference = await db.UserPreferences.FindAsync([tenantId, userObjectId], cancellationToken);
        if (preference is null)
        {
            db.UserPreferences.Add(new UserPreference
            {
                TenantId = tenantId,
                UserObjectId = userObjectId,
                Theme = theme,
                UpdatedAt = DateTimeOffset.UtcNow
            });
        }
        else
        {
            preference.Theme = theme;
            preference.UpdatedAt = DateTimeOffset.UtcNow;
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
