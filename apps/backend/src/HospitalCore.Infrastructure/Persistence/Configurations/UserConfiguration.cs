using HospitalCore.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HospitalCore.Infrastructure.Persistence.Configurations;

public class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("users");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");

        builder.Property(x => x.Cccd)
            .HasColumnName("cccd")
            .HasMaxLength(12)
            .IsRequired();

        builder.HasIndex(x => x.Cccd)
            .IsUnique()
            .HasDatabaseName("ix_users_cccd");

        builder.Property(x => x.FullName)
            .HasColumnName("full_name")
            .HasMaxLength(255)
            .IsRequired();

        builder.Property(x => x.Role)
            .HasColumnName("role")
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(x => x.HospitalId)
            .HasColumnName("hospital_id")
            .IsRequired();

        builder.Property(x => x.MaCchn)
            .HasColumnName("ma_cchn")
            .HasMaxLength(50);

        builder.Property(x => x.IsActive)
            .HasColumnName("is_active")
            .HasDefaultValue(true)
            .IsRequired();

        builder.Property(x => x.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        builder.Property(x => x.UpdatedAt)
            .HasColumnName("updated_at")
            .IsRequired();

        builder.HasOne(x => x.Hospital)
            .WithMany(h => h.Users)
            .HasForeignKey(x => x.HospitalId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
