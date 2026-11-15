using HospitalCore.Application.Common.Interfaces.Security;
using HospitalCore.Application.Common.Mediator;

namespace HospitalCore.Application.Features.Auth.Logout;

public record LogoutCommand(string SessionId) : IRequest<bool>;

public class LogoutCommandHandler : IRequestHandler<LogoutCommand, bool>
{
    private readonly ISessionManager _sessionManager;
    private readonly IAuditService _auditService;

    public LogoutCommandHandler(ISessionManager sessionManager, IAuditService auditService)
    {
        _sessionManager = sessionManager;
        _auditService = auditService;
    }

    public async Task<bool> Handle(LogoutCommand request, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(request.SessionId))
        {
            var existing = await _sessionManager.GetSessionAsync(request.SessionId, cancellationToken);
            await _sessionManager.RemoveSessionAsync(request.SessionId, cancellationToken);
            await _auditService.RecordAsync(
                existing?.UserId,
                "LOGOUT",
                "UserSession",
                request.SessionId,
                new { maCskcb = existing?.MaCskcb },
                cancellationToken);
        }

        return true;
    }
}
