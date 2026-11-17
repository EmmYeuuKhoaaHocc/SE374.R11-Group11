namespace HospitalCore.Application.Common.Interfaces.Security;

public interface IAuditService
{
    Task RecordAsync(
        Guid? userId,
        string action,
        string resourceType,
        string resourceId,
        object? payload = null,
        CancellationToken ct = default);

    Task RecordFailureAsync(
        Guid? userId,
        string action,
        string resourceType,
        string resourceId,
        object? payload = null,
        CancellationToken ct = default);
}
