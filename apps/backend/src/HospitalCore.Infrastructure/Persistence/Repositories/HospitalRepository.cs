using HospitalCore.Application.Common.Interfaces.Repositories;
using HospitalCore.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace HospitalCore.Infrastructure.Persistence.Repositories;

public class HospitalRepository : IHospitalRepository
{
    private readonly HospitalDbContext _context;

    public HospitalRepository(HospitalDbContext context)
    {
        _context = context;
    }

    public Task<Hospital?> GetByIdAsync(Guid hospitalId, CancellationToken ct = default)
    {
        return _context.Hospitals.FirstOrDefaultAsync(x => x.Id == hospitalId, ct);
    }

    public Task<Hospital?> GetByMaCskcbAsync(string maCskcb, CancellationToken ct = default)
    {
        var normalized = maCskcb.Trim();
        return _context.Hospitals.FirstOrDefaultAsync(x => x.MaCskcb == normalized, ct);
    }

    public async Task<IReadOnlyList<Hospital>> GetAllAsync(CancellationToken ct = default)
    {
        return await _context.Hospitals.OrderBy(x => x.Name).ToListAsync(ct);
    }

    public Task AddAsync(Hospital hospital, CancellationToken ct = default)
    {
        return _context.Hospitals.AddAsync(hospital, ct).AsTask();
    }

    public Task UpdateAsync(Hospital hospital, CancellationToken ct = default)
    {
        _context.Hospitals.Update(hospital);
        return Task.CompletedTask;
    }
}
