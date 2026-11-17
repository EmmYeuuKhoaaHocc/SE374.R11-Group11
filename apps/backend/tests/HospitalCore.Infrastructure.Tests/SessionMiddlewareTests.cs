using System.Security.Claims;
using HospitalCore.Api.Endpoints;
using HospitalCore.Api.Middleware;
using HospitalCore.Application.Common.Interfaces.Security;
using HospitalCore.Application.Features.Auth.Login;
using HospitalCore.Domain.Entities;
using HospitalCore.Domain.Enums;
using HospitalCore.Infrastructure.Persistence;
using HospitalCore.Infrastructure.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace HospitalCore.Infrastructure.Tests;

public class SessionMiddlewareTests
{
    [Fact]
    public async Task OidcTransactionStore_ConsumesOnceAndValidatesBindingToken()
    {
        var cache = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));
        var store = new OidcTransactionStore(cache);
        var bindingHash = LoginQueryHandler.ComputeBindingHash("bind-token-1");

        await store.SaveAsync("state-1", new OidcTransaction
        {
            Nonce = "n1",
            CodeVerifier = "cv1",
            RedirectUri = "http://localhost:3000/auth/callback",
            ReturnUrl = "/dashboard",
            BindingHash = bindingHash
        }, TimeSpan.FromMinutes(10));

        var wrongBinding = await store.ConsumeAsync("state-1", "wrong-token");
        Assert.Null(wrongBinding);

        var replayAfterFail = await store.ConsumeAsync("state-1", "bind-token-1");
        Assert.Null(replayAfterFail);

        await store.SaveAsync("state-2", new OidcTransaction
        {
            Nonce = "n2",
            CodeVerifier = "cv2",
            RedirectUri = "http://localhost:3000/auth/callback",
            ReturnUrl = "/cases",
            BindingHash = LoginQueryHandler.ComputeBindingHash("bind-token-2")
        }, TimeSpan.FromMinutes(10));

        var valid = await store.ConsumeAsync("state-2", "bind-token-2");
        Assert.NotNull(valid);
        Assert.Equal("/cases", valid!.ReturnUrl);

        var replayAfterSuccess = await store.ConsumeAsync("state-2", "bind-token-2");
        Assert.Null(replayAfterSuccess);
    }

    [Fact]
    public async Task AuditService_RecordFailureAsync_PersistsLogInIndependentScopeWithFkFallback()
    {
        var dbName = Guid.NewGuid().ToString("N");
        var services = new ServiceCollection();
        services.AddDbContext<HospitalDbContext>(o => o.UseInMemoryDatabase(dbName));
        services.AddHttpContextAccessor();
        services.AddScoped<IAuditService, AuditService>();

        await using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var audit = scope.ServiceProvider.GetRequiredService<IAuditService>();
        var missingUserId = Guid.NewGuid();

        await audit.RecordFailureAsync(missingUserId, "LOGIN_DENIED", "Auth", "079095001122", new { code = "NO_CCHN_PERMISSION" });

        await using var verifyScope = provider.CreateAsyncScope();
        var db = verifyScope.ServiceProvider.GetRequiredService<HospitalDbContext>();
        var saved = await db.AuditLogs.SingleAsync();
        Assert.Null(saved.UserId);
        Assert.Equal("LOGIN_DENIED", saved.Action);
        Assert.Contains("NO_CCHN_PERMISSION", saved.PayloadSnapshot);
        Assert.Contains(missingUserId.ToString(), saved.PayloadSnapshot);
    }

    [Fact]
    public async Task SessionAuthenticationMiddleware_EvictsSessionWhenUserSuspended()
    {
        var dbName = Guid.NewGuid().ToString("N");
        var services = new ServiceCollection();
        services.AddDbContext<HospitalDbContext>(o => o.UseInMemoryDatabase(dbName));
        services.AddMemoryCache();
        services.AddDistributedMemoryCache();
        services.AddSingleton<ISessionManager, DistributedSessionManager>();
        services.AddScoped<IActiveStatusChecker, ActiveStatusChecker>();

        await using var provider = services.BuildServiceProvider();
        var hospital = new Hospital("79024", "Cho Ray", "HCM", isActive: true);
        var user = new User("079095001122", "Dr. Active", UserRole.DOCTOR_USER, hospital.Id, "CCHN-1", isActive: false);

        await using (var seedScope = provider.CreateAsyncScope())
        {
            var db = seedScope.ServiceProvider.GetRequiredService<HospitalDbContext>();
            db.Hospitals.Add(hospital);
            db.Users.Add(user);
            await db.SaveChangesAsync();
        }

        var sessionManager = provider.GetRequiredService<ISessionManager>();
        await sessionManager.SaveSessionAsync(new UserSession
        {
            SessionId = "sess-suspended",
            UserId = user.Id,
            Cccd = user.Cccd,
            FullName = user.FullName,
            Role = user.Role.ToString(),
            HospitalId = hospital.Id,
            MaCskcb = hospital.MaCskcb,
            HospitalName = hospital.Name,
            MaCchn = user.MaCchn,
            AccessToken = "at",
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(8)
        }, TimeSpan.FromHours(8));

        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers.Cookie = $"{AuthEndpoints.SessionCookieName}=sess-suspended";

        var nextCalled = false;
        var middleware = new SessionAuthenticationMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        }, NullLogger<SessionAuthenticationMiddleware>.Instance);

        await using var reqScope = provider.CreateAsyncScope();
        var activeChecker = reqScope.ServiceProvider.GetRequiredService<IActiveStatusChecker>();
        await middleware.InvokeAsync(httpContext, sessionManager, activeChecker);

        Assert.True(nextCalled);
        Assert.False(httpContext.User.Identity?.IsAuthenticated == true);
        Assert.Null(await sessionManager.GetSessionAsync("sess-suspended"));
        Assert.Contains(AuthEndpoints.SessionCookieName, httpContext.Response.Headers.SetCookie.ToString());
    }

    [Fact]
    public async Task AuthorizationPolicies_EnforceRoleHierarchy()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAuthorization(options =>
        {
            options.AddPolicy(AppAuthorizationPolicies.RequireDoctor, p =>
                p.RequireRole("DOCTOR_USER", "HOSPITAL_ADMIN", "MOH_ADMIN"));
            options.AddPolicy(AppAuthorizationPolicies.RequireHospitalAdmin, p =>
                p.RequireRole("HOSPITAL_ADMIN", "MOH_ADMIN"));
            options.AddPolicy(AppAuthorizationPolicies.RequireMohAdmin, p =>
                p.RequireRole("MOH_ADMIN"));
        });

        await using var provider = services.BuildServiceProvider();
        var authService = provider.GetRequiredService<IAuthorizationService>();

        var doctor = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Role, "DOCTOR_USER") }, "SessionCookie"));
        var hospitalAdmin = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Role, "HOSPITAL_ADMIN") }, "SessionCookie"));
        var mohAdmin = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Role, "MOH_ADMIN") }, "SessionCookie"));

        Assert.True((await authService.AuthorizeAsync(doctor, AppAuthorizationPolicies.RequireDoctor)).Succeeded);
        Assert.False((await authService.AuthorizeAsync(doctor, AppAuthorizationPolicies.RequireHospitalAdmin)).Succeeded);
        Assert.True((await authService.AuthorizeAsync(hospitalAdmin, AppAuthorizationPolicies.RequireHospitalAdmin)).Succeeded);
        Assert.False((await authService.AuthorizeAsync(hospitalAdmin, AppAuthorizationPolicies.RequireMohAdmin)).Succeeded);
        Assert.True((await authService.AuthorizeAsync(mohAdmin, AppAuthorizationPolicies.RequireMohAdmin)).Succeeded);
    }
}
