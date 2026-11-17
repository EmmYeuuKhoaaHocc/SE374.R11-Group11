using HospitalCore.Application.Common.Interfaces.Security;
using HospitalCore.Application.Features.Auth.GetCurrentUser;
using HospitalCore.Application.Features.Auth.Logout;
using Xunit;

namespace HospitalCore.Application.Tests;

public class LogoutAndCurrentUserTests
{
    [Fact]
    public async Task Logout_ExistingSession_RemovesSessionAndAuditsLogout()
    {
        var userId = Guid.NewGuid();
        var sessionManager = new InMemorySessionManager(new UserSession
        {
            SessionId = "sess-1",
            UserId = userId,
            MaCskcb = "79024"
        });
        var audit = new RecordingAuditService();

        var handler = new LogoutCommandHandler(sessionManager, audit);
        var result = await handler.Handle(new LogoutCommand("sess-1"), CancellationToken.None);

        Assert.True(result);
        Assert.Null(await sessionManager.GetSessionAsync("sess-1"));
        Assert.Single(audit.Events);
        Assert.Equal("LOGOUT", audit.Events[0].Action);
        Assert.Equal(userId, audit.Events[0].UserId);
    }

    [Fact]
    public async Task GetCurrentUser_AuthenticatedContext_IncludesHospitalName()
    {
        var context = new StubCurrentUserContext
        {
            IsAuthenticated = true,
            UserId = Guid.NewGuid(),
            Cccd = "001200099999",
            FullName = "BS. TRẦN MINH TÂM",
            Role = "DOCTOR_USER",
            HospitalId = Guid.NewGuid(),
            MaCskcb = "79024",
            HospitalName = "Bệnh viện Chợ Rẫy",
            MaCchn = "004512/BYT-CCHN"
        };

        var handler = new GetCurrentUserQueryHandler(context);
        var dto = await handler.Handle(new GetCurrentUserQuery(), CancellationToken.None);

        Assert.Equal("Bệnh viện Chợ Rẫy", dto.HospitalName);
        Assert.Equal("79024", dto.MaCskcb);
        Assert.Equal("DOCTOR_USER", dto.Role);
    }

    private sealed class InMemorySessionManager(UserSession initial) : ISessionManager
    {
        private readonly Dictionary<string, UserSession> _sessions = new() { [initial.SessionId] = initial };

        public Task<UserSession?> GetSessionAsync(string sessionId, CancellationToken ct = default) =>
            Task.FromResult(_sessions.GetValueOrDefault(sessionId));

        public Task SaveSessionAsync(UserSession session, TimeSpan lifetime, CancellationToken ct = default)
        {
            _sessions[session.SessionId] = session;
            return Task.CompletedTask;
        }

        public Task RemoveSessionAsync(string sessionId, CancellationToken ct = default)
        {
            _sessions.Remove(sessionId);
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingAuditService : IAuditService
    {
        public List<(Guid? UserId, string Action)> Events { get; } = new();

        public Task RecordAsync(Guid? userId, string action, string resourceType, string resourceId, object? payload = null, CancellationToken ct = default)
        {
            Events.Add((userId, action));
            return Task.CompletedTask;
        }

        public Task RecordFailureAsync(Guid? userId, string action, string resourceType, string resourceId, object? payload = null, CancellationToken ct = default)
        {
            Events.Add((userId, action));
            return Task.CompletedTask;
        }
    }

    private sealed class StubCurrentUserContext : ICurrentUserContext
    {
        public Guid? UserId { get; init; }
        public string? Cccd { get; init; }
        public string? FullName { get; init; }
        public string? Role { get; init; }
        public Guid? HospitalId { get; init; }
        public string? MaCskcb { get; init; }
        public string? HospitalName { get; init; }
        public string? MaCchn { get; init; }
        public bool IsAuthenticated { get; init; }
    }
}
