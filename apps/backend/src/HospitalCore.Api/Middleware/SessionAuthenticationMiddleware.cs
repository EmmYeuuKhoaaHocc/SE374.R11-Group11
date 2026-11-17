using System.Security.Claims;
using System.Text.Encodings.Web;
using HospitalCore.Api.Endpoints;
using HospitalCore.Application.Common.Interfaces.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace HospitalCore.Api.Middleware;

public static class SessionCookieDefaults
{
    public const string AuthenticationScheme = "SessionCookie";
}

public class SessionAuthenticationMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<SessionAuthenticationMiddleware> _logger;

    public SessionAuthenticationMiddleware(
        RequestDelegate next,
        ILogger<SessionAuthenticationMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(
        HttpContext context,
        ISessionManager sessionManager,
        IActiveStatusChecker activeStatusChecker)
    {
        if (context.Request.Cookies.TryGetValue(AuthEndpoints.SessionCookieName, out var sessionId) &&
            !string.IsNullOrWhiteSpace(sessionId))
        {
            var session = await sessionManager.GetSessionAsync(sessionId, context.RequestAborted);
            if (session != null)
            {
                if (session.ExpiresAt <= DateTimeOffset.UtcNow)
                {
                    await EvictSessionAsync(context, sessionManager, sessionId);
                }
                else if (!await activeStatusChecker.IsUserAndHospitalActiveAsync(
                             session.UserId,
                             session.HospitalId,
                             context.RequestAborted))
                {
                    _logger.LogInformation(
                        "Evicting session '{SessionId}' because user '{UserId}' or hospital '{HospitalId}' is inactive.",
                        sessionId,
                        session.UserId,
                        session.HospitalId);
                    await EvictSessionAsync(context, sessionManager, sessionId);
                }
                else
                {
                    context.User = BuildPrincipal(session);
                }
            }
        }

        await _next(context);
    }

    private static async Task EvictSessionAsync(
        HttpContext context,
        ISessionManager sessionManager,
        string sessionId)
    {
        await sessionManager.RemoveSessionAsync(sessionId, context.RequestAborted);
        context.Response.Cookies.Delete(AuthEndpoints.SessionCookieName, new CookieOptions
        {
            Path = "/",
            SameSite = SameSiteMode.Lax,
            Secure = context.Request.IsHttps
        });
    }

    private static ClaimsPrincipal BuildPrincipal(UserSession session)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, session.UserId.ToString()),
            new("cccd", session.Cccd),
            new(ClaimTypes.Name, session.FullName),
            new(ClaimTypes.Role, session.Role),
            new("hospital_id", session.HospitalId.ToString()),
            new("ma_cskcb", session.MaCskcb),
            new("hospital_name", session.HospitalName)
        };

        if (!string.IsNullOrWhiteSpace(session.MaCchn))
        {
            claims.Add(new Claim("ma_cchn", session.MaCchn));
        }

        var identity = new ClaimsIdentity(claims, SessionCookieDefaults.AuthenticationScheme);
        return new ClaimsPrincipal(identity);
    }
}

public class SessionCookieAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public SessionCookieAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (Context.User.Identity?.IsAuthenticated == true)
        {
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(Context.User, Scheme.Name)));
        }

        return Task.FromResult(AuthenticateResult.NoResult());
    }

    protected override async Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        Response.ContentType = "application/problem+json";
        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status401Unauthorized,
            Title = "Unauthorized",
            Detail = "Authentication is required to access this resource."
        };
        problem.Extensions["code"] = "UNAUTHORIZED";
        await Response.WriteAsJsonAsync(problem);
    }

    protected override async Task HandleForbiddenAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status403Forbidden;
        Response.ContentType = "application/problem+json";
        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status403Forbidden,
            Title = "Forbidden",
            Detail = "You do not have permission to perform this action."
        };
        problem.Extensions["code"] = "FORBIDDEN";
        await Response.WriteAsJsonAsync(problem);
    }
}
