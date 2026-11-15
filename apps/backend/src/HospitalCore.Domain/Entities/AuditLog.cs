namespace HospitalCore.Domain.Entities;

public class AuditLog
{
    public long Id { get; private set; }
    public Guid? UserId { get; private set; }
    public string Action { get; private set; } = string.Empty;
    public string ResourceType { get; private set; } = string.Empty;
    public string ResourceId { get; private set; } = string.Empty;
    public string IpAddress { get; private set; } = string.Empty;
    public string? UserAgent { get; private set; }
    public string? PayloadSnapshot { get; private set; }
    public DateTimeOffset Timestamp { get; private set; }

    public User? User { get; private set; }

    protected AuditLog() { }

    public AuditLog(
        Guid? userId,
        string action,
        string resourceType,
        string resourceId,
        string ipAddress,
        string? userAgent = null,
        string? payloadSnapshot = null)
    {
        UserId = userId;
        Action = action.Trim();
        ResourceType = resourceType.Trim();
        ResourceId = resourceId.Trim();
        IpAddress = ipAddress.Trim();
        UserAgent = userAgent?.Trim();
        PayloadSnapshot = payloadSnapshot;
        Timestamp = DateTimeOffset.UtcNow;
    }
}
