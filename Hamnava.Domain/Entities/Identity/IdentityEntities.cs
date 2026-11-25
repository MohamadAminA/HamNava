using Hamnava.Domain.Enums;

namespace Hamnava.Domain.Entities.Identity;

public class User : AuditableSoftDeleteEntity
{
    public string MobileNumber { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string? EnglishFullName { get; set; }
    public string? PublicSlug { get; set; }
    public string? NationalCode { get; set; }

    /// <summary>Stored in Gregorian UTC DateOnly; converted to/from Jalali via IJalaliDateService.</summary>
    public DateOnly? BirthDateGregorian { get; set; }
    public string? Address { get; set; }
    public string? Bio { get; set; }
    public string? ProfileImagePath { get; set; }

    public UserStatus Status { get; set; } = UserStatus.Active;
    public bool IsPublicProfileVisible { get; set; }

    // Referral graph
    public string ReferralCode { get; set; } = string.Empty;
    public Guid? ReferredByUserId { get; set; }
    public User? ReferredByUser { get; set; }
    public ICollection<User> ReferredUsers { get; set; } = new List<User>();

    // Primary instrument (for students) & teacher-specific financial override
    public Guid? PrimaryInstrumentId { get; set; }
    public decimal? CustomTeacherCommissionPercentage { get; set; }
    public ReferrerCommissionType? CustomReferrerCommissionType { get; set; }
    public decimal? CustomReferrerCommissionValue { get; set; }

    // Explicit quota start date for fixed 60-day rolling periods (independent of enrollment renewal)
    public DateOnly? QuotaStartDateGregorian { get; set; }

    // Navigation collections
    public ICollection<UserRole> UserRoles { get; set; } = new List<UserRole>();
    public ICollection<OtpRequest> OtpRequests { get; set; } = new List<OtpRequest>();
    public ICollection<UserSession> Sessions { get; set; } = new List<UserSession>();
    public ICollection<UserConsent> Consents { get; set; } = new List<UserConsent>();
    public ICollection<TeacherInstrument> TeacherInstruments { get; set; } = new List<TeacherInstrument>();
    public ICollection<TeacherStudentAssignment> AssignedStudents { get; set; } = new List<TeacherStudentAssignment>();
    public ICollection<TeacherStudentAssignment> AssignedTeachers { get; set; } = new List<TeacherStudentAssignment>();

    public string FullName => $"{FirstName} {LastName}".Trim();
}

public class Role : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public string DisplayNameFa { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsSystemRole { get; set; } = true;

    public ICollection<UserRole> UserRoles { get; set; } = new List<UserRole>();
}

public class UserRole : BaseEntity
{
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;

    public Guid RoleId { get; set; }
    public Role Role { get; set; } = null!;

    public Guid? AssignedByUserId { get; set; }
}

public class TeacherInstrument : BaseEntity
{
    public Guid TeacherUserId { get; set; }
    public User Teacher { get; set; } = null!;

    public Guid InstrumentId { get; set; }
    public Instruments.Instrument Instrument { get; set; } = null!;
}

/// <summary>
/// Authoritative teacher-student ownership link used by IAuthorizationScopeService
/// to enforce strict server-side horizontal data isolation.
/// </summary>
public class TeacherStudentAssignment : BaseEntity
{
    public Guid TeacherUserId { get; set; }
    public User Teacher { get; set; } = null!;

    public Guid StudentUserId { get; set; }
    public User Student { get; set; } = null!;

    public Guid InstrumentId { get; set; }
    public Instruments.Instrument Instrument { get; set; } = null!;

    public bool IsActive { get; set; } = true;
    public DateTime AssignedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? EndedAtUtc { get; set; }
    public string? TransferReason { get; set; }
    public Guid? AssignedByUserId { get; set; }
}

public class OtpRequest : BaseEntity
{
    public string MobileNumber { get; set; } = string.Empty;
    public Guid? UserId { get; set; }
    public User? User { get; set; }

    /// <summary>HMAC-SHA256 hash of OTP + salt + pepper. Plaintext OTP is never persisted.</summary>
    public string CodeHash { get; set; } = string.Empty;
    public string Salt { get; set; } = string.Empty;

    public DateTime ExpiresAtUtc { get; set; }
    public int FailedAttempts { get; set; }
    public int MaxAttempts { get; set; }
    public bool IsUsed { get; set; }
    public DateTime? UsedAtUtc { get; set; }
    public string RequestIpAddress { get; set; } = string.Empty;
    public string Purpose { get; set; } = "Login";
}

public class UserSession : BaseEntity
{
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;

    public string SessionTokenHash { get; set; } = string.Empty;
    public string IpAddress { get; set; } = string.Empty;
    public string UserAgent { get; set; } = string.Empty;
    public DateTime ExpiresAtUtc { get; set; }
    public bool IsRevoked { get; set; }
    public DateTime? RevokedAtUtc { get; set; }
    public DateTime LastActivityAtUtc { get; set; } = DateTime.UtcNow;
}

public class UserConsent : BaseEntity
{
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;

    public ConsentType ConsentType { get; set; }
    public bool IsGranted { get; set; }
    public string ConsentVersion { get; set; } = "1.0";
    public DateTime RecordedAtUtc { get; set; } = DateTime.UtcNow;
    public string? IpAddress { get; set; }
    public Guid? RecordedByUserId { get; set; }
}
