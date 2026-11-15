namespace HospitalCore.Application.Common.Interfaces.Security;

public interface IActiveStatusChecker
{
    Task<bool> IsUserAndHospitalActiveAsync(Guid userId, Guid hospitalId, CancellationToken ct = default);
}
