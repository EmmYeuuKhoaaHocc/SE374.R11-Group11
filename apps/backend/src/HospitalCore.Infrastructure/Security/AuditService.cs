using System.Text.Json;
using HospitalCore.Application.Common.Interfaces.Security;
using HospitalCore.Domain.Entities;
using HospitalCore.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HospitalCore.Infrastructure.Security;

public class AuditService : IAuditService
{
    private readonly HospitalDbContext _dbContext;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public AuditService(
        HospitalDbContext dbContext,
        IServiceScopeFactory scopeFactory,
        IHttpContextAccessor httpContextAccessor)
    {
        _dbContext = dbContext;
        _scopeFactory = scopeFactory;
        _httpContextAccessor = httpContextAccessor;
    }

    public async Task RecordAsync(
        Guid? userId,
        string action,
        string resourceType,
        string resourceId,
        object? payload = null,
        CancellationToken ct = default)
    {
        var auditLog = CreateAuditLog(userId, action, resourceType, resourceId, payload);
        await _dbContext.AuditLogs.AddAsync(auditLog, ct);
        await _dbContext.SaveChangesAsync(ct);
    }

    public async Task RecordFailureAsync(
        Guid? userId,
        string action,
        string resourceType,
        string resourceId,
        object? payload = null,
        CancellationToken ct = default)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HospitalDbContext>();

        var effectiveUserId = userId;
        var effectivePayload = payload;
        if (effectiveUserId.HasValue)
        {
            var userExists = await dbContext.Users.AnyAsync(u => u.Id == effectiveUserId.Value, ct);
            if (!userExists)
            {
                effectivePayload = new
                {
                    unverifiedUserId = effectiveUserId.Value,
                    details = payload
                };
                effectiveUserId = null;
            }
        }

        var auditLog = CreateAuditLog(effectiveUserId, action, resourceType, resourceId, effectivePayload);
        await dbContext.AuditLogs.AddAsync(auditLog, ct);
        await dbContext.SaveChangesAsync(ct);
    }

    private AuditLog CreateAuditLog(
        Guid? userId,
        string action,
        string resourceType,
        string resourceId,
        object? payload)
    {
        var httpContext = _httpContextAccessor.HttpContext;
        var ipAddress = httpContext?.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var userAgent = httpContext?.Request.Headers.UserAgent.ToString();
        if (string.IsNullOrWhiteSpace(userAgent))
        {
            userAgent = null;
        }

        var payloadJson = payload == null ? null : JsonSerializer.Serialize(payload);

        return new AuditLog(
            userId,
            action,
            resourceType,
            resourceId,
            ipAddress,
            userAgent,
            payloadJson);
    }
}
