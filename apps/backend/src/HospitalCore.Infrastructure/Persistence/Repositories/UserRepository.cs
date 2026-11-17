using HospitalCore.Application.Common.Interfaces.Repositories;
using HospitalCore.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace HospitalCore.Infrastructure.Persistence.Repositories;

public class UserRepository : IUserRepository
{
    private readonly HospitalDbContext _context;

    public UserRepository(HospitalDbContext context)
    {
        _context = context;
    }

    public Task<User?> GetByIdAsync(Guid userId, CancellationToken ct = default)
    {
        return _context.Users
            .Include(x => x.Hospital)
            .FirstOrDefaultAsync(x => x.Id == userId, ct);
    }

    public Task<User?> GetByCccdAsync(string cccd, CancellationToken ct = default)
    {
        var normalized = cccd.Trim();
        return _context.Users
            .Include(x => x.Hospital)
            .FirstOrDefaultAsync(x => x.Cccd == normalized, ct);
    }

    public Task AddAsync(User user, CancellationToken ct = default)
    {
        return _context.Users.AddAsync(user, ct).AsTask();
    }

    public Task UpdateAsync(User user, CancellationToken ct = default)
    {
        _context.Users.Update(user);
        return Task.CompletedTask;
    }
}
