using Hamnava.Domain.Entities.Identity;
using Hamnava.Domain.Entities.Instruments;
using Hamnava.Domain.Enums;

namespace Hamnava.Domain.Entities.Education;

public class CoursePlan : AuditableSoftDeleteEntity
{
    public string Title { get; set; } = string.Empty;
    public string? Subtitle { get; set; }
    public string? Description { get; set; }
    public string? FeaturesJson { get; set; }

    public int LessonDurationMinutes { get; set; }
    public int CoursePeriodMonths { get; set; }
    public int SessionCount { get; set; }

    /// <summary>Authoritative base price in IRR.</summary>
    public decimal BasePriceIrr { get; set; }

    /// <summary>Optional public display/promotional price in IRR (shown on /pricing).</summary>
    public decimal? PublicDisplayPriceIrr { get; set; }

    public bool IsDiscountEligible { get; set; } = true;
    public bool IsFeatured { get; set; }
    public string? BadgeText { get; set; }
    public bool IsActive { get; set; } = true;
    public int DisplayOrder { get; set; }

    public DateOnly EffectiveFromGregorian { get; set; }
    public DateOnly? ExpiresAtGregorian { get; set; }

    public ICollection<Enrollment> Enrollments { get; set; } = new List<Enrollment>();
}

public class Enrollment : AuditableSoftDeleteEntity
{
    public Guid StudentUserId { get; set; }
    public User Student { get; set; } = null!;

    public Guid TeacherUserId { get; set; }
    public User Teacher { get; set; } = null!;

    public Guid InstrumentId { get; set; }
    public Instrument Instrument { get; set; } = null!;

    public Guid CoursePlanId { get; set; }
    public CoursePlan CoursePlan { get; set; } = null!;

    public Guid RoomId { get; set; }
    public Room Room { get; set; } = null!;

    public EnrollmentStatus Status { get; set; } = EnrollmentStatus.PendingPayment;

    public DateOnly StartDateGregorian { get; set; }
    public DateOnly? EndDateGregorian { get; set; }

    // Commercial & Financial Term Snapshots (immutable after purchase so future plan edits never alter history)
    public string PlanTitleSnapshot { get; set; } = string.Empty;
    public int LessonDurationMinutesSnapshot { get; set; }
    public int CoursePeriodMonthsSnapshot { get; set; }
    public int TotalSessionsSnapshot { get; set; }
    public int RemainingSessions { get; set; }

    public decimal OriginalPriceIrr { get; set; }
    public decimal DiscountAmountIrr { get; set; }
    public decimal FinalPaidAmountIrr { get; set; }

    public decimal TeacherCommissionPercentageSnapshot { get; set; }
    public decimal AcademyCommissionPercentageSnapshot { get; set; }
    public ReferrerCommissionType? ReferrerCommissionTypeSnapshot { get; set; }
    public decimal? ReferrerCommissionValueSnapshot { get; set; }
    public CommissionCalculationBasis CommissionBasisSnapshot { get; set; } = CommissionCalculationBasis.PostDiscountFinalAmount;
    public FinancialRoundingMode RoundingModeSnapshot { get; set; } = FinancialRoundingMode.AwayFromZeroToNearest1000Irr;

    public Guid? DiscountCodeId { get; set; }
    public Guid? PaymentId { get; set; }

    public ICollection<ClassSession> Sessions { get; set; } = new List<ClassSession>();
}

public class ClassSession : AuditableSoftDeleteEntity
{
    public Guid EnrollmentId { get; set; }
    public Enrollment Enrollment { get; set; } = null!;

    public Guid StudentUserId { get; set; }
    public User Student { get; set; } = null!;

    public Guid TeacherUserId { get; set; }
    public User Teacher { get; set; } = null!;

    public Guid InstrumentId { get; set; }
    public Instrument Instrument { get; set; } = null!;

    public Guid RoomId { get; set; }
    public Room Room { get; set; } = null!;

    public int SessionNumber { get; set; }
    public ClassSessionType SessionType { get; set; } = ClassSessionType.Regular;
    public ClassSessionStatus Status { get; set; } = ClassSessionStatus.Scheduled;

    /// <summary>Stored in UTC for unambiguous server-side 24h deadline & conflict calculation.</summary>
    public DateTime StartAtUtc { get; set; }
    public DateTime EndAtUtc { get; set; }
    public int DurationMinutes { get; set; }

    /// <summary>Links a make-up or rescheduled session back to the original session.</summary>
    public Guid? OriginalSessionId { get; set; }
    public ClassSession? OriginalSession { get; set; }

    /// <summary>Links to the TeacherTimeSlot consumed (if student rescheduled or booked make-up).</summary>
    public Guid? TeacherTimeSlotId { get; set; }
    public TeacherTimeSlot? TeacherTimeSlot { get; set; }

    /// <summary>Prevents duplicate session-based financial credits.</summary>
    public bool IsFinanciallySettled { get; set; }
    public DateTime? FinanciallySettledAtUtc { get; set; }

    /// <summary>Tracks Hangfire scheduled reminder job IDs so rescheduling cancels stale reminders.</summary>
    public string? ReminderHangfireJobIdsJson { get; set; }

    public ICollection<AttendanceRecord> AttendanceRecords { get; set; } = new List<AttendanceRecord>();
    public ICollection<SessionScheduleHistory> ScheduleHistories { get; set; } = new List<SessionScheduleHistory>();
    public ICollection<PracticeNote> PracticeNotes { get; set; } = new List<PracticeNote>();
    public ICollection<PracticeAssignment> PracticeAssignments { get; set; } = new List<PracticeAssignment>();
}

public class SessionScheduleHistory : BaseEntity
{
    public Guid ClassSessionId { get; set; }
    public ClassSession ClassSession { get; set; } = null!;

    public DateTime OriginalStartAtUtc { get; set; }
    public DateTime OriginalEndAtUtc { get; set; }
    public Guid OriginalRoomId { get; set; }

    public DateTime NewStartAtUtc { get; set; }
    public DateTime NewEndAtUtc { get; set; }
    public Guid NewRoomId { get; set; }

    public Guid ChangedByUserId { get; set; }
    public User ChangedByUser { get; set; } = null!;

    public string ChangeReason { get; set; } = string.Empty;
    public Guid? RelatedRequestId { get; set; }
}

public class TeacherTimeSlot : AuditableSoftDeleteEntity
{
    public Guid TeacherUserId { get; set; }
    public User Teacher { get; set; } = null!;

    public Guid RoomId { get; set; }
    public Room Room { get; set; } = null!;

    public DateTime StartAtUtc { get; set; }
    public DateTime EndAtUtc { get; set; }

    public AvailabilitySlotType AvailabilityType { get; set; } = AvailabilitySlotType.Both;
    public bool IsActive { get; set; } = true;
    public bool IsClosed { get; set; }
    public string? Notes { get; set; }
}

public class StudentQuotaPeriod : BaseEntity
{
    public Guid StudentUserId { get; set; }
    public User Student { get; set; } = null!;

    public int PeriodIndex { get; set; }
    public DateOnly PeriodStartDateGregorian { get; set; }
    public DateOnly PeriodEndDateGregorian { get; set; }

    public int MaxAuthorizedAbsencesSnapshot { get; set; }
    public int MaxRescheduleRequestsSnapshot { get; set; }
    public int MaxMakeupSelectionsSnapshot { get; set; }

    public ICollection<StudentQuotaUsage> Usages { get; set; } = new List<StudentQuotaUsage>();
}

public class StudentQuotaUsage : BaseEntity
{
    public Guid StudentQuotaPeriodId { get; set; }
    public StudentQuotaPeriod QuotaPeriod { get; set; } = null!;

    public Guid StudentUserId { get; set; }
    public User Student { get; set; } = null!;

    public QuotaUsageType UsageType { get; set; }

    /// <summary>+1 for consumption, -1 for reversal, or negative delta for administrative reset.</summary>
    public int Delta { get; set; }

    public Guid? RelatedSessionId { get; set; }
    public Guid? RelatedRequestId { get; set; }

    public Guid PerformedByUserId { get; set; }
    public User PerformedByUser { get; set; } = null!;

    public string? PreviousStateSnapshotJson { get; set; }
    public string? NewStateSnapshotJson { get; set; }
    public string Reason { get; set; } = string.Empty;
}

public class AttendanceRecord : BaseEntity
{
    public Guid ClassSessionId { get; set; }
    public ClassSession ClassSession { get; set; } = null!;

    public ClassSessionStatus PreviousStatus { get; set; }
    public ClassSessionStatus NewStatus { get; set; }

    public Guid RecordedByUserId { get; set; }
    public User RecordedByUser { get; set; } = null!;

    public DateTime RecordedAtUtc { get; set; } = DateTime.UtcNow;
    public string? Notes { get; set; }
    public bool TriggeredFinancialSettlement { get; set; }
    public bool TriggeredCompensatingReversal { get; set; }
}

public class MakeupLessonRequest : BaseEntity
{
    public Guid StudentUserId { get; set; }
    public User Student { get; set; } = null!;

    public Guid OriginalSessionId { get; set; }
    public ClassSession OriginalSession { get; set; } = null!;

    public Guid? SelectedSlotId { get; set; }
    public TeacherTimeSlot? SelectedSlot { get; set; }

    public Guid? CreatedMakeupSessionId { get; set; }
    public ClassSession? CreatedMakeupSession { get; set; }

    public RequestWorkflowStatus Status { get; set; } = RequestWorkflowStatus.Pending;
    public bool IsManualTeacherOverride { get; set; }
    public Guid? ProcessedByUserId { get; set; }
    public DateTime? ProcessedAtUtc { get; set; }
    public string? Reason { get; set; }
    public string? RejectionReason { get; set; }
}

public class RescheduleRequest : BaseEntity
{
    public Guid StudentUserId { get; set; }
    public User Student { get; set; } = null!;

    public Guid ClassSessionId { get; set; }
    public ClassSession ClassSession { get; set; } = null!;

    public Guid TargetTimeSlotId { get; set; }
    public TeacherTimeSlot TargetTimeSlot { get; set; } = null!;

    public DateTime OriginalStartAtUtc { get; set; }
    public DateTime RequestedStartAtUtc { get; set; }
    public DateTime RequestedEndAtUtc { get; set; }

    public RequestWorkflowStatus Status { get; set; } = RequestWorkflowStatus.Pending;
    public Guid? ReviewedByUserId { get; set; }
    public DateTime? ReviewedAtUtc { get; set; }
    public string? StudentNote { get; set; }
    public string? ReviewNote { get; set; }
}

public class PracticeNote : AuditableSoftDeleteEntity
{
    public Guid ClassSessionId { get; set; }
    public ClassSession ClassSession { get; set; } = null!;

    public Guid StudentUserId { get; set; }
    public User Student { get; set; } = null!;

    public Guid TeacherUserId { get; set; }
    public User Teacher { get; set; } = null!;

    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string? PracticeRecommendations { get; set; }
    public bool IsVisibleToStudent { get; set; } = true;
}

public class PracticeAssignment : AuditableSoftDeleteEntity
{
    public Guid ClassSessionId { get; set; }
    public ClassSession ClassSession { get; set; } = null!;

    public Guid StudentUserId { get; set; }
    public User Student { get; set; } = null!;

    public Guid TeacherUserId { get; set; }
    public User Teacher { get; set; } = null!;

    public string Title { get; set; } = string.Empty;
    public string Instructions { get; set; } = string.Empty;
    public DateOnly? DueDateGregorian { get; set; }
    public bool IsCompletedByStudent { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public string? StudentReflectionNote { get; set; }
}

public class AcademicHoliday : AuditableSoftDeleteEntity
{
    public DateOnly HolidayDateGregorian { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool GrantsMakeupEligibility { get; set; } = true;
}

public class ConsultationPlan : AuditableSoftDeleteEntity
{
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int DurationMinutes { get; set; }
    public decimal PriceIrr { get; set; }
    public int MaxBookingsPerSlot { get; set; } = 1;
    public bool IsActive { get; set; } = true;
    public int DisplayOrder { get; set; }

    public ICollection<ConsultationPlanConsultant> Consultants { get; set; } = new List<ConsultationPlanConsultant>();
    public ICollection<ConsultationBooking> Bookings { get; set; } = new List<ConsultationBooking>();
}

public class ConsultationPlanConsultant : BaseEntity
{
    public Guid ConsultationPlanId { get; set; }
    public ConsultationPlan ConsultationPlan { get; set; } = null!;

    public Guid ConsultantUserId { get; set; }
    public User Consultant { get; set; } = null!;
}

public class ConsultationBooking : AuditableSoftDeleteEntity
{
    public Guid ConsultationPlanId { get; set; }
    public ConsultationPlan ConsultationPlan { get; set; } = null!;

    public Guid ConsultantUserId { get; set; }
    public User Consultant { get; set; } = null!;

    public Guid? TeacherTimeSlotId { get; set; }
    public TeacherTimeSlot? TeacherTimeSlot { get; set; }

    public Guid? UserId { get; set; }
    public User? User { get; set; }

    public string VisitorFullName { get; set; } = string.Empty;
    public string VisitorMobileNumber { get; set; } = string.Empty;
    public string? Notes { get; set; }

    public DateTime StartAtUtc { get; set; }
    public DateTime EndAtUtc { get; set; }
    public decimal PriceSnapshotIrr { get; set; }

    public ConsultationBookingStatus Status { get; set; } = ConsultationBookingStatus.PendingPayment;
    public Guid? PaymentId { get; set; }
}

public class LearningCertificate : BaseEntity
{
    public Guid StudentUserId { get; set; }
    public User Student { get; set; } = null!;

    public Guid EnrollmentId { get; set; }
    public Enrollment Enrollment { get; set; } = null!;

    public string CertificateNumber { get; set; } = string.Empty;

    /// <summary>Cryptographically random, non-predictable verification token for public QR verification.</summary>
    public string VerificationToken { get; set; } = string.Empty;

    public string StudentFullNameSnapshot { get; set; } = string.Empty;
    public string TeacherFullNameSnapshot { get; set; } = string.Empty;
    public string InstrumentNameSnapshot { get; set; } = string.Empty;
    public string CourseTitleSnapshot { get; set; } = string.Empty;
    public int CompletedSessionsSnapshot { get; set; }
    public DateOnly CourseStartDateGregorian { get; set; }
    public DateOnly CourseCompletionDateGregorian { get; set; }
    public DateTime IssuedAtUtc { get; set; } = DateTime.UtcNow;
    public bool IsRevoked { get; set; }
}
