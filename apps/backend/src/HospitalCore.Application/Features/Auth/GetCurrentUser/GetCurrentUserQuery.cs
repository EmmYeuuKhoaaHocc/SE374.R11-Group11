using HospitalCore.Application.Common.Exceptions;
using HospitalCore.Application.Common.Interfaces.Security;
using HospitalCore.Application.Common.Mediator;

namespace HospitalCore.Application.Features.Auth.GetCurrentUser;

public record GetCurrentUserQuery : IRequest<CurrentUserDto>;

public record CurrentUserDto(
    Guid UserId,
    string Cccd,
    string FullName,
    string Role,
    Guid HospitalId,
    string? MaCskcb,
    string? HospitalName,
    string? MaCchn);

public class GetCurrentUserQueryHandler : IRequestHandler<GetCurrentUserQuery, CurrentUserDto>
{
    private readonly ICurrentUserContext _currentUserContext;

    public GetCurrentUserQueryHandler(ICurrentUserContext currentUserContext)
    {
        _currentUserContext = currentUserContext;
    }

    public Task<CurrentUserDto> Handle(GetCurrentUserQuery request, CancellationToken cancellationToken)
    {
        if (!_currentUserContext.IsAuthenticated || _currentUserContext.UserId == null)
        {
            throw new ApplicationValidationException("Auth", "User is not authenticated.");
        }

        var dto = new CurrentUserDto(
            _currentUserContext.UserId.Value,
            _currentUserContext.Cccd ?? string.Empty,
            _currentUserContext.FullName ?? string.Empty,
            _currentUserContext.Role ?? string.Empty,
            _currentUserContext.HospitalId ?? Guid.Empty,
            _currentUserContext.MaCskcb,
            _currentUserContext.HospitalName,
            _currentUserContext.MaCchn);

        return Task.FromResult(dto);
    }
}
