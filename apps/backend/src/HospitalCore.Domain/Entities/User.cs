using HospitalCore.Domain.Common;
using HospitalCore.Domain.Enums;

namespace HospitalCore.Domain.Entities;

public class User : BaseEntity
{
    public string Cccd { get; private set; } = string.Empty;
    public string FullName { get; private set; } = string.Empty;
    public UserRole Role { get; private set; }
    public Guid HospitalId { get; private set; }
    public string? MaCchn { get; private set; }
    public bool IsActive { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public Hospital Hospital { get; private set; } = null!;

    protected User() : base() { }

    public User(string cccd, string fullName, UserRole role, Guid hospitalId, string? maCchn = null, bool isActive = true)
        : base()
    {
        Cccd = cccd.Trim();
        FullName = fullName.Trim();
        Role = role;
        HospitalId = hospitalId;
        MaCchn = string.IsNullOrWhiteSpace(maCchn) ? null : maCchn.Trim().ToUpperInvariant();
        IsActive = isActive;
        CreatedAt = DateTimeOffset.UtcNow;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void UpdateProfile(string fullName, string? maCchn)
    {
        FullName = fullName.Trim();
        MaCchn = string.IsNullOrWhiteSpace(maCchn) ? null : maCchn.Trim().ToUpperInvariant();
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void AssignHospital(Guid hospitalId)
    {
        HospitalId = hospitalId;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void SetActive(bool isActive)
    {
        IsActive = isActive;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
