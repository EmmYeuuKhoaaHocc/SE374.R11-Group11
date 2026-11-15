namespace HospitalCore.Application.Common.Interfaces.Security;

public record UserSession
{
    public string SessionId { get; init; } = string.Empty;
    public Guid UserId { get; init; }
    public string Cccd { get; init; } = string.Empty;
    public string FullName { get; init; } = string.Empty;
    public string Role { get; init; } = string.Empty;
    public Guid HospitalId { get; init; }
    public string MaCskcb { get; init; } = string.Empty;
    public string HospitalName { get; init; } = string.Empty;
    public string? MaCchn { get; init; }
    public string AccessToken { get; init; } = string.Empty;
    public string? RefreshToken { get; init; }
    public string? IdToken { get; init; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset ExpiresAt { get; init; }
}

public interface ISessionManager
{
    Task<UserSession?> GetSessionAsync(string sessionId, CancellationToken ct = default);
    Task SaveSessionAsync(UserSession session, TimeSpan lifetime, CancellationToken ct = default);
    Task RemoveSessionAsync(string sessionId, CancellationToken ct = default);
}
