using Hamnava.Domain.Entities.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Hamnava.DataLayer.Configurations;

public class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("Users");
        builder.HasKey(u => u.Id);

        builder.Property(u => u.MobileNumber).HasMaxLength(15).IsRequired();
        builder.Property(u => u.FirstName).HasMaxLength(100).IsRequired();
        builder.Property(u => u.LastName).HasMaxLength(100).IsRequired();
        builder.Property(u => u.EnglishFullName).HasMaxLength(200);
        builder.Property(u => u.PublicSlug).HasMaxLength(150);
        builder.Property(u => u.NationalCode).HasMaxLength(15);
        builder.Property(u => u.Address).HasMaxLength(500);
        builder.Property(u => u.Bio).HasMaxLength(2000);
        builder.Property(u => u.ProfileImagePath).HasMaxLength(500);
        builder.Property(u => u.ReferralCode).HasMaxLength(32).IsRequired();

        builder.Property(u => u.CustomTeacherCommissionPercentage).HasPrecision(5, 2);
        builder.Property(u => u.CustomReferrerCommissionValue).HasPrecision(18, 2);

        builder.HasIndex(u => u.MobileNumber).IsUnique();
        builder.HasIndex(u => u.ReferralCode).IsUnique();
        builder.HasIndex(u => u.PublicSlug).IsUnique().HasFilter("[PublicSlug] IS NOT NULL");
        builder.HasIndex(u => u.NationalCode).IsUnique().HasFilter("[NationalCode] IS NOT NULL");
        builder.HasIndex(u => new { u.Status, u.IsPublicProfileVisible });

        builder.HasOne(u => u.ReferredByUser)
            .WithMany(u => u.ReferredUsers)
            .HasForeignKey(u => u.ReferredByUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class RoleConfiguration : IEntityTypeConfiguration<Role>
{
    public void Configure(EntityTypeBuilder<Role> builder)
    {
        builder.ToTable("Roles");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Name).HasMaxLength(50).IsRequired();
        builder.Property(r => r.DisplayNameFa).HasMaxLength(100).IsRequired();
        builder.Property(r => r.Description).HasMaxLength(300);
        builder.HasIndex(r => r.Name).IsUnique();
    }
}

public class UserRoleConfiguration : IEntityTypeConfiguration<UserRole>
{
    public void Configure(EntityTypeBuilder<UserRole> builder)
    {
        builder.ToTable("UserRoles");
        builder.HasKey(ur => ur.Id);
        builder.HasIndex(ur => new { ur.UserId, ur.RoleId }).IsUnique();

        builder.HasOne(ur => ur.User)
            .WithMany(u => u.UserRoles)
            .HasForeignKey(ur => ur.UserId);

        builder.HasOne(ur => ur.Role)
            .WithMany(r => r.UserRoles)
            .HasForeignKey(ur => ur.RoleId);
    }
}

public class TeacherInstrumentConfiguration : IEntityTypeConfiguration<TeacherInstrument>
{
    public void Configure(EntityTypeBuilder<TeacherInstrument> builder)
    {
        builder.ToTable("TeacherInstruments");
        builder.HasKey(ti => ti.Id);
        builder.HasIndex(ti => new { ti.TeacherUserId, ti.InstrumentId }).IsUnique();

        builder.HasOne(ti => ti.Teacher)
            .WithMany(u => u.TeacherInstruments)
            .HasForeignKey(ti => ti.TeacherUserId);

        builder.HasOne(ti => ti.Instrument)
            .WithMany(i => i.Teachers)
            .HasForeignKey(ti => ti.InstrumentId);
    }
}

public class TeacherStudentAssignmentConfiguration : IEntityTypeConfiguration<TeacherStudentAssignment>
{
    public void Configure(EntityTypeBuilder<TeacherStudentAssignment> builder)
    {
        builder.ToTable("TeacherStudentAssignments");
        builder.HasKey(a => a.Id);

        builder.Property(a => a.TransferReason).HasMaxLength(500);
        builder.HasIndex(a => new { a.TeacherUserId, a.StudentUserId, a.IsActive });
        builder.HasIndex(a => new { a.StudentUserId, a.IsActive });

        builder.HasOne(a => a.Teacher)
            .WithMany(u => u.AssignedStudents)
            .HasForeignKey(a => a.TeacherUserId);

        builder.HasOne(a => a.Student)
            .WithMany(u => u.AssignedTeachers)
            .HasForeignKey(a => a.StudentUserId);

        builder.HasOne(a => a.Instrument)
            .WithMany()
            .HasForeignKey(a => a.InstrumentId);
    }
}

public class OtpRequestConfiguration : IEntityTypeConfiguration<OtpRequest>
{
    public void Configure(EntityTypeBuilder<OtpRequest> builder)
    {
        builder.ToTable("OtpRequests");
        builder.HasKey(o => o.Id);

        builder.Property(o => o.MobileNumber).HasMaxLength(15).IsRequired();
        builder.Property(o => o.CodeHash).HasMaxLength(128).IsRequired();
        builder.Property(o => o.Salt).HasMaxLength(64).IsRequired();
        builder.Property(o => o.RequestIpAddress).HasMaxLength(64).IsRequired();
        builder.Property(o => o.Purpose).HasMaxLength(50).IsRequired();

        builder.HasIndex(o => new { o.MobileNumber, o.CreatedAtUtc });
        builder.HasIndex(o => new { o.RequestIpAddress, o.CreatedAtUtc });
        builder.HasIndex(o => o.ExpiresAtUtc);
    }
}

public class UserSessionConfiguration : IEntityTypeConfiguration<UserSession>
{
    public void Configure(EntityTypeBuilder<UserSession> builder)
    {
        builder.ToTable("UserSessions");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.SessionTokenHash).HasMaxLength(128).IsRequired();
        builder.Property(s => s.IpAddress).HasMaxLength(64).IsRequired();
        builder.Property(s => s.UserAgent).HasMaxLength(500);
        builder.HasIndex(s => s.SessionTokenHash).IsUnique();
        builder.HasIndex(s => new { s.UserId, s.IsRevoked, s.ExpiresAtUtc });
    }
}

public class UserConsentConfiguration : IEntityTypeConfiguration<UserConsent>
{
    public void Configure(EntityTypeBuilder<UserConsent> builder)
    {
        builder.ToTable("UserConsents");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.ConsentVersion).HasMaxLength(20).IsRequired();
        builder.Property(c => c.IpAddress).HasMaxLength(64);
        builder.HasIndex(c => new { c.UserId, c.ConsentType, c.RecordedAtUtc });
    }
}
