using HospitalCore.Domain.Common;

namespace HospitalCore.Domain.Entities;

public class Hospital : BaseAggregateRoot
{
    public string MaCskcb { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public string Address { get; private set; } = string.Empty;
    public bool IsActive { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public ICollection<User> Users { get; private set; } = new List<User>();

    protected Hospital() : base() { }

    public Hospital(string maCskcb, string name, string address, bool isActive = true) : base()
    {
        MaCskcb = maCskcb.Trim();
        Name = name.Trim();
        Address = address.Trim();
        IsActive = isActive;
        CreatedAt = DateTimeOffset.UtcNow;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void UpdateStatus(bool isActive)
    {
        IsActive = isActive;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void UpdateInfo(string name, string address)
    {
        Name = name.Trim();
        Address = address.Trim();
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
