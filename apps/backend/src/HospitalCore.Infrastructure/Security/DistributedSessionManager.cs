using System.Text.Json;
using HospitalCore.Application.Common.Interfaces.Security;
using Microsoft.Extensions.Caching.Distributed;

namespace HospitalCore.Infrastructure.Security;

public class DistributedSessionManager : ISessionManager
{
    private const string KeyPrefix = "session:";
    private readonly IDistributedCache _cache;

    public DistributedSessionManager(IDistributedCache cache)
    {
        _cache = cache;
    }

    public async Task<UserSession?> GetSessionAsync(string sessionId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return null;
        }

        var json = await _cache.GetStringAsync($"{KeyPrefix}{sessionId}", ct);
        return string.IsNullOrWhiteSpace(json) ? null : JsonSerializer.Deserialize<UserSession>(json);
    }

    public Task SaveSessionAsync(UserSession session, TimeSpan lifetime, CancellationToken ct = default)
    {
        var json = JsonSerializer.Serialize(session);
        return _cache.SetStringAsync(
            $"{KeyPrefix}{session.SessionId}",
            json,
            new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = lifetime },
            ct);
    }

    public Task RemoveSessionAsync(string sessionId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return Task.CompletedTask;
        }

        return _cache.RemoveAsync($"{KeyPrefix}{sessionId}", ct);
    }
}
