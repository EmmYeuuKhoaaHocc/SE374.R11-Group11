using HospitalCore.Application.Common.Mediator;
using HospitalCore.Application.Features.Auth.Callback;
using HospitalCore.Application.Features.Auth.GetCurrentUser;
using HospitalCore.Application.Features.Auth.Login;
using HospitalCore.Application.Features.Auth.Logout;
using Microsoft.AspNetCore.Mvc;

namespace HospitalCore.Api.Endpoints;

public record ExchangeSessionRequest(string Code, string? State, string? RedirectUri = null);

public record ExchangeSessionResponse(
    Guid UserId,
    string FullName,
    string Role,
    string MaCskcb,
    string HospitalName,
    string? MaCchn,
    string ReturnUrl);

public class AuthEndpoints : IEndpoint
{
    public const string SessionCookieName = "NeuroScanSession";
    public const string OidcTxCookieName = "NeuroScanOidcTx";

    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth").WithTags("Authentication");

        group.MapGet("/login", LoginAsync).AllowAnonymous();
        group.MapPost("/exchange-session", ExchangeSessionAsync).AllowAnonymous();
        group.MapPost("/logout", LogoutAsync).AllowAnonymous();

        app.MapGet("/api/me", GetCurrentUserAsync)
            .RequireAuthorization()
            .WithTags("Authentication");
    }

    private static async Task<IResult> LoginAsync(
        [FromServices] IMediator mediator,
        HttpContext httpContext,
        [FromQuery] string? returnUrl,
        [FromQuery] string? redirectUri,
        CancellationToken ct)
    {
        var bindingToken = Guid.NewGuid().ToString("N");
        httpContext.Response.Cookies.Append(OidcTxCookieName, bindingToken, new CookieOptions
        {
            HttpOnly = true,
            SameSite = SameSiteMode.Lax,
            Secure = httpContext.Request.IsHttps,
            Path = "/api/auth",
            MaxAge = TimeSpan.FromMinutes(10)
        });

        var result = await mediator.Send(new LoginQuery(returnUrl, redirectUri, bindingToken), ct);
        return Results.Redirect(result.RedirectUrl);
    }

    private static async Task<IResult> ExchangeSessionAsync(
        [FromServices] IMediator mediator,
        HttpContext httpContext,
        [FromBody] ExchangeSessionRequest request,
        CancellationToken ct)
    {
        httpContext.Request.Cookies.TryGetValue(OidcTxCookieName, out var bindingToken);
        httpContext.Response.Cookies.Delete(OidcTxCookieName, new CookieOptions
        {
            Path = "/api/auth",
            SameSite = SameSiteMode.Lax,
            Secure = httpContext.Request.IsHttps
        });

        var result = await mediator.Send(
            new CallbackCommand(request.Code, request.State, bindingToken, request.RedirectUri),
            ct);

        httpContext.Response.Cookies.Append(SessionCookieName, result.SessionId, new CookieOptions
        {
            HttpOnly = true,
            SameSite = SameSiteMode.Lax,
            Secure = httpContext.Request.IsHttps,
            Path = "/",
            MaxAge = TimeSpan.FromHours(8)
        });

        return Results.Ok(new ExchangeSessionResponse(
            result.UserId,
            result.FullName,
            result.Role,
            result.MaCskcb,
            result.HospitalName,
            result.MaCchn,
            result.ReturnUrl));
    }

    private static async Task<IResult> LogoutAsync(
        [FromServices] IMediator mediator,
        HttpContext httpContext,
        CancellationToken ct)
    {
        if (httpContext.Request.Cookies.TryGetValue(SessionCookieName, out var sessionId) &&
            !string.IsNullOrWhiteSpace(sessionId))
        {
            await mediator.Send(new LogoutCommand(sessionId), ct);
        }

        httpContext.Response.Cookies.Delete(SessionCookieName, new CookieOptions
        {
            Path = "/",
            SameSite = SameSiteMode.Lax,
            Secure = httpContext.Request.IsHttps
        });

        return Results.NoContent();
    }

    private static async Task<IResult> GetCurrentUserAsync(
        [FromServices] IMediator mediator,
        CancellationToken ct)
    {
        var currentUser = await mediator.Send(new GetCurrentUserQuery(), ct);
        return Results.Ok(currentUser);
    }
}
