using HospitalCore.Domain.Entities;

namespace HospitalCore.Application.Common.Interfaces.Repositories;

public interface IHospitalRepository
{
    Task<Hospital?> GetByIdAsync(Guid hospitalId, CancellationToken ct = default);
    Task<Hospital?> GetByMaCskcbAsync(string maCskcb, CancellationToken ct = default);
    Task<IReadOnlyList<Hospital>> GetAllAsync(CancellationToken ct = default);
    Task AddAsync(Hospital hospital, CancellationToken ct = default);
    Task UpdateAsync(Hospital hospital, CancellationToken ct = default);
}
