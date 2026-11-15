namespace HospitalCore.Application.Common.Interfaces.Security;

public interface ICurrentUserContext
{
    Guid? UserId { get; }
    string? Cccd { get; }
    string? FullName { get; }
    string? Role { get; }
    Guid? HospitalId { get; }
    string? MaCskcb { get; }
    string? HospitalName { get; }
    string? MaCchn { get; }
    bool IsAuthenticated { get; }
}
