using System.Security.Claims;
using HospitalCore.Application.Common.Interfaces.Security;
using Microsoft.AspNetCore.Http;

namespace HospitalCore.Infrastructure.Security;

public class CurrentUserContext : ICurrentUserContext
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CurrentUserContext(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    private ClaimsPrincipal? Principal => _httpContextAccessor.HttpContext?.User;

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated == true;

    public Guid? UserId => Guid.TryParse(Principal?.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;

    public string? Cccd => Principal?.FindFirstValue("cccd");

    public string? FullName => Principal?.FindFirstValue(ClaimTypes.Name);

    public string? Role => Principal?.FindFirstValue(ClaimTypes.Role);

    public Guid? HospitalId => Guid.TryParse(Principal?.FindFirstValue("hospital_id"), out var id) ? id : null;

    public string? MaCskcb => Principal?.FindFirstValue("ma_cskcb");

    public string? HospitalName => Principal?.FindFirstValue("hospital_name");

    public string? MaCchn => Principal?.FindFirstValue("ma_cchn");
}
