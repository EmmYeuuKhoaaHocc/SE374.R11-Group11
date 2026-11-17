using HospitalCore.Application.Common.Interfaces.Security;
using HospitalCore.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;

namespace HospitalCore.Infrastructure.Security;

public class ActiveStatusChecker : IActiveStatusChecker
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(45);
    private readonly IMemoryCache _cache;
    private readonly IServiceScopeFactory _scopeFactory;

    public ActiveStatusChecker(IMemoryCache cache, IServiceScopeFactory scopeFactory)
    {
        _cache = cache;
        _scopeFactory = scopeFactory;
    }

    public async Task<bool> IsUserAndHospitalActiveAsync(Guid userId, Guid hospitalId, CancellationToken ct = default)
    {
        var cacheKey = $"auth:active:{userId:N}:{hospitalId:N}";
        if (_cache.TryGetValue(cacheKey, out bool cachedActive))
        {
            return cachedActive;
        }

        await using var scope = _scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HospitalDbContext>();

        var status = await dbContext.Users
            .AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new
            {
                u.IsActive,
                u.HospitalId,
                HospitalActive = u.Hospital.IsActive
            })
            .FirstOrDefaultAsync(ct);

        var isActive = status != null
            && status.IsActive
            && status.HospitalId == hospitalId
            && status.HospitalActive;

        _cache.Set(cacheKey, isActive, CacheDuration);
        return isActive;
    }
}
