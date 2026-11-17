using HospitalCore.Api.Endpoints;
using HospitalCore.Api.Middleware;
using HospitalCore.Application;
using HospitalCore.Application.Common.Interfaces.Security;
using HospitalCore.Domain.Entities;
using HospitalCore.Domain.Enums;
using HospitalCore.Infrastructure;
using HospitalCore.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = SessionCookieDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = SessionCookieDefaults.AuthenticationScheme;
    options.DefaultForbidScheme = SessionCookieDefaults.AuthenticationScheme;
})
.AddScheme<AuthenticationSchemeOptions, SessionCookieAuthenticationHandler>(
    SessionCookieDefaults.AuthenticationScheme,
    _ => { });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(AppAuthorizationPolicies.RequireDoctor, policy =>
        policy.RequireRole(
            UserRole.DOCTOR_USER.ToString(),
            UserRole.HOSPITAL_ADMIN.ToString(),
            UserRole.MOH_ADMIN.ToString()));

    options.AddPolicy(AppAuthorizationPolicies.RequireHospitalAdmin, policy =>
        policy.RequireRole(
            UserRole.HOSPITAL_ADMIN.ToString(),
            UserRole.MOH_ADMIN.ToString()));

    options.AddPolicy(AppAuthorizationPolicies.RequireMohAdmin, policy =>
        policy.RequireRole(UserRole.MOH_ADMIN.ToString()));
});

var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>();
if (allowedOrigins == null || allowedOrigins.Length == 0)
{
    var fallbackOrigin = builder.Configuration["Frontend:Url"] ?? "http://localhost:3000";
    allowedOrigins = new[] { fallbackOrigin.TrimEnd('/') };
}

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.WithOrigins(allowedOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    });
});

builder.Services.AddOpenApi();

var app = builder.Build();

app.UseMiddleware<GlobalExceptionHandlingMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
    await EnsureDevelopmentDatabaseSeededAsync(app.Services);
}

app.UseCors();
app.UseMiddleware<SessionAuthenticationMiddleware>();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health", () => Results.Ok(new { status = "healthy", service = "backend" }))
    .AllowAnonymous();

new AuthEndpoints().MapEndpoints(app);

app.Run();

static async Task EnsureDevelopmentDatabaseSeededAsync(IServiceProvider services)
{
    await using var scope = services.CreateAsyncScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<HospitalDbContext>();

    if (dbContext.Database.IsRelational())
    {
        await dbContext.Database.MigrateAsync();
    }

    var hospital = await dbContext.Hospitals.FirstOrDefaultAsync(h => h.MaCskcb == "79024");
    if (hospital == null)
    {
        hospital = new Hospital(
            "79024",
            "Benh vien Cho Ray",
            "201B Nguyen Chi Thanh, Phuong 12, Quan 5, TP. Ho Chi Minh",
            isActive: true);
        await dbContext.Hospitals.AddAsync(hospital);
        await dbContext.SaveChangesAsync();
    }

    await SeedDevUserIfMissingAsync(
        dbContext,
        "001200055555",
        "BS. NGUYỄN VĂN ĐỨC",
        UserRole.HOSPITAL_ADMIN,
        hospital.Id,
        "003344/BYT-CCHN");

    await SeedDevUserIfMissingAsync(
        dbContext,
        "001200044444",
        "BS. VŨ THỊ HƯƠNG",
        UserRole.MOH_ADMIN,
        hospital.Id,
        "007788/BYT-CCHN");
}

static async Task SeedDevUserIfMissingAsync(
    HospitalDbContext dbContext,
    string cccd,
    string fullName,
    UserRole role,
    Guid hospitalId,
    string maCchn)
{
    var exists = await dbContext.Users.AnyAsync(u => u.Cccd == cccd);
    if (exists)
    {
        return;
    }

    var user = new User(cccd, fullName, role, hospitalId, maCchn, isActive: true);
    await dbContext.Users.AddAsync(user);
    await dbContext.SaveChangesAsync();
}

public partial class Program;
