using Hamnava.Domain.Entities.Education;
using Hamnava.Domain.Entities.Instruments;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Hamnava.DataLayer.Configurations;

public class InstrumentCategoryConfiguration : IEntityTypeConfiguration<InstrumentCategory>
{
    public void Configure(EntityTypeBuilder<InstrumentCategory> builder)
    {
        builder.ToTable("InstrumentCategories");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Name).HasMaxLength(120).IsRequired();
        builder.Property(c => c.Slug).HasMaxLength(120).IsRequired();
        builder.Property(c => c.Description).HasMaxLength(500);
        builder.HasIndex(c => c.Slug).IsUnique();
    }
}

public class InstrumentConfiguration : IEntityTypeConfiguration<Instrument>
{
    public void Configure(EntityTypeBuilder<Instrument> builder)
    {
        builder.ToTable("Instruments", t =>
        {
            t.HasCheckConstraint("CK_Instruments_TotalInventory_NonNegative", "[TotalInventoryQuantity] >= 0");
            t.HasCheckConstraint("CK_Instruments_AvailableInventory_Valid", "[AvailableInventoryQuantity] >= 0 AND [AvailableInventoryQuantity] <= [TotalInventoryQuantity]");
        });
        builder.HasKey(i => i.Id);
        builder.Property(i => i.Name).HasMaxLength(120).IsRequired();
        builder.Property(i => i.Slug).HasMaxLength(120).IsRequired();
        builder.Property(i => i.Description).HasMaxLength(1000);
        builder.Property(i => i.ImagePath).HasMaxLength(500);
        builder.Property(i => i.DailyRentalPriceIrr).HasPrecision(18, 0);
        builder.Property(i => i.MonthlyRentalPriceIrr).HasPrecision(18, 0);
        builder.HasIndex(i => i.Slug).IsUnique();
        builder.HasIndex(i => new { i.IsActive, i.IsRentable, i.DisplayOrder });
    }
}

public class InstrumentRentalConfiguration : IEntityTypeConfiguration<InstrumentRental>
{
    public void Configure(EntityTypeBuilder<InstrumentRental> builder)
    {
        builder.ToTable("InstrumentRentals");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.UnitPriceSnapshotIrr).HasPrecision(18, 0);
        builder.Property(r => r.OriginalAmountIrr).HasPrecision(18, 0);
        builder.Property(r => r.DiscountAmountIrr).HasPrecision(18, 0);
        builder.Property(r => r.FinalPayableAmountIrr).HasPrecision(18, 0);
        builder.Property(r => r.Notes).HasMaxLength(1000);
        builder.HasIndex(r => new { r.StudentUserId, r.Status });
        builder.HasIndex(r => new { r.Status, r.EndDateGregorian });
    }
}

public class InstrumentRentalRenewalConfiguration : IEntityTypeConfiguration<InstrumentRentalRenewal>
{
    public void Configure(EntityTypeBuilder<InstrumentRentalRenewal> builder)
    {
        builder.ToTable("InstrumentRentalRenewals");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.AmountIrr).HasPrecision(18, 0);
    }
}

public class RoomConfiguration : IEntityTypeConfiguration<Room>
{
    public void Configure(EntityTypeBuilder<Room> builder)
    {
        builder.ToTable("Rooms", t =>
            t.HasCheckConstraint("CK_Rooms_Capacity_Positive", "[Capacity] > 0"));
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Name).HasMaxLength(120).IsRequired();
        builder.Property(r => r.Description).HasMaxLength(500);
        builder.HasIndex(r => r.Name).IsUnique();
    }
}

public class RoomSupportedInstrumentConfiguration : IEntityTypeConfiguration<RoomSupportedInstrument>
{
    public void Configure(EntityTypeBuilder<RoomSupportedInstrument> builder)
    {
        builder.ToTable("RoomSupportedInstruments");
        builder.HasKey(ri => ri.Id);
        builder.HasIndex(ri => new { ri.RoomId, ri.InstrumentId }).IsUnique();
    }
}

public class CoursePlanConfiguration : IEntityTypeConfiguration<CoursePlan>
{
    public void Configure(EntityTypeBuilder<CoursePlan> builder)
    {
        builder.ToTable("CoursePlans", t =>
        {
            t.HasCheckConstraint("CK_CoursePlans_Duration_Positive", "[LessonDurationMinutes] > 0");
            t.HasCheckConstraint("CK_CoursePlans_Sessions_Positive", "[SessionCount] > 0");
            t.HasCheckConstraint("CK_CoursePlans_BasePrice_NonNegative", "[BasePriceIrr] >= 0");
        });
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Title).HasMaxLength(200).IsRequired();
        builder.Property(p => p.Subtitle).HasMaxLength(300);
        builder.Property(p => p.Description).HasMaxLength(2000);
        builder.Property(p => p.BadgeText).HasMaxLength(80);
        builder.Property(p => p.BasePriceIrr).HasPrecision(18, 0);
        builder.Property(p => p.PublicDisplayPriceIrr).HasPrecision(18, 0);
        builder.HasIndex(p => new { p.IsActive, p.DisplayOrder });
    }
}

public class EnrollmentConfiguration : IEntityTypeConfiguration<Enrollment>
{
    public void Configure(EntityTypeBuilder<Enrollment> builder)
    {
        builder.ToTable("Enrollments");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.PlanTitleSnapshot).HasMaxLength(200).IsRequired();
        builder.Property(e => e.OriginalPriceIrr).HasPrecision(18, 0);
        builder.Property(e => e.DiscountAmountIrr).HasPrecision(18, 0);
        builder.Property(e => e.FinalPaidAmountIrr).HasPrecision(18, 0);
        builder.Property(e => e.TeacherCommissionPercentageSnapshot).HasPrecision(5, 2);
        builder.Property(e => e.AcademyCommissionPercentageSnapshot).HasPrecision(5, 2);
        builder.Property(e => e.ReferrerCommissionValueSnapshot).HasPrecision(18, 2);

        builder.HasIndex(e => new { e.StudentUserId, e.Status });
        builder.HasIndex(e => new { e.TeacherUserId, e.Status });
    }
}

public class ClassSessionConfiguration : IEntityTypeConfiguration<ClassSession>
{
    public void Configure(EntityTypeBuilder<ClassSession> builder)
    {
        builder.ToTable("ClassSessions", t =>
            t.HasCheckConstraint("CK_ClassSessions_EndAfterStart", "[EndAtUtc] > [StartAtUtc]"));
        builder.HasKey(s => s.Id);
        builder.Property(s => s.ReminderHangfireJobIdsJson).HasMaxLength(1000);

        // Critical scheduling conflict & calendar range indexes
        builder.HasIndex(s => new { s.TeacherUserId, s.StartAtUtc, s.EndAtUtc });
        builder.HasIndex(s => new { s.StudentUserId, s.StartAtUtc, s.EndAtUtc });
        builder.HasIndex(s => new { s.RoomId, s.StartAtUtc, s.EndAtUtc });
        builder.HasIndex(s => new { s.EnrollmentId, s.SessionNumber });

        builder.HasOne(s => s.OriginalSession)
            .WithMany()
            .HasForeignKey(s => s.OriginalSessionId);
    }
}

public class TeacherTimeSlotConfiguration : IEntityTypeConfiguration<TeacherTimeSlot>
{
    public void Configure(EntityTypeBuilder<TeacherTimeSlot> builder)
    {
        builder.ToTable("TeacherTimeSlots", t =>
            t.HasCheckConstraint("CK_TeacherTimeSlots_EndAfterStart", "[EndAtUtc] > [StartAtUtc]"));
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Notes).HasMaxLength(500);
        builder.HasIndex(t => new { t.TeacherUserId, t.IsActive, t.IsClosed, t.StartAtUtc });
        builder.HasIndex(t => new { t.RoomId, t.StartAtUtc, t.EndAtUtc });
    }
}

public class StudentQuotaPeriodConfiguration : IEntityTypeConfiguration<StudentQuotaPeriod>
{
    public void Configure(EntityTypeBuilder<StudentQuotaPeriod> builder)
    {
        builder.ToTable("StudentQuotaPeriods");
        builder.HasKey(q => q.Id);
        builder.HasIndex(q => new { q.StudentUserId, q.PeriodIndex }).IsUnique();
        builder.HasIndex(q => new { q.StudentUserId, q.PeriodStartDateGregorian, q.PeriodEndDateGregorian });
    }
}

public class StudentQuotaUsageConfiguration : IEntityTypeConfiguration<StudentQuotaUsage>
{
    public void Configure(EntityTypeBuilder<StudentQuotaUsage> builder)
    {
        builder.ToTable("StudentQuotaUsages");
        builder.HasKey(u => u.Id);
        builder.Property(u => u.Reason).HasMaxLength(500).IsRequired();
        builder.HasIndex(u => new { u.StudentQuotaPeriodId, u.UsageType });
        builder.HasIndex(u => new { u.StudentUserId, u.CreatedAtUtc });
    }
}

public class MakeupLessonRequestConfiguration : IEntityTypeConfiguration<MakeupLessonRequest>
{
    public void Configure(EntityTypeBuilder<MakeupLessonRequest> builder)
    {
        builder.ToTable("MakeupLessonRequests");
        builder.HasKey(m => m.Id);
        builder.Property(m => m.Reason).HasMaxLength(500);
        builder.Property(m => m.RejectionReason).HasMaxLength(500);
        builder.HasIndex(m => new { m.StudentUserId, m.OriginalSessionId, m.Status });
    }
}

public class RescheduleRequestConfiguration : IEntityTypeConfiguration<RescheduleRequest>
{
    public void Configure(EntityTypeBuilder<RescheduleRequest> builder)
    {
        builder.ToTable("RescheduleRequests");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.StudentNote).HasMaxLength(500);
        builder.Property(r => r.ReviewNote).HasMaxLength(500);
        builder.HasIndex(r => new { r.StudentUserId, r.Status });
        builder.HasIndex(r => r.ClassSessionId);
    }
}

public class ConsultationPlanConfiguration : IEntityTypeConfiguration<ConsultationPlan>
{
    public void Configure(EntityTypeBuilder<ConsultationPlan> builder)
    {
        builder.ToTable("ConsultationPlans");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Title).HasMaxLength(200).IsRequired();
        builder.Property(c => c.Description).HasMaxLength(1500);
        builder.Property(c => c.PriceIrr).HasPrecision(18, 0);
    }
}

public class ConsultationPlanConsultantConfiguration : IEntityTypeConfiguration<ConsultationPlanConsultant>
{
    public void Configure(EntityTypeBuilder<ConsultationPlanConsultant> builder)
    {
        builder.ToTable("ConsultationPlanConsultants");
        builder.HasKey(c => c.Id);
        builder.HasIndex(c => new { c.ConsultationPlanId, c.ConsultantUserId }).IsUnique();
    }
}

public class ConsultationBookingConfiguration : IEntityTypeConfiguration<ConsultationBooking>
{
    public void Configure(EntityTypeBuilder<ConsultationBooking> builder)
    {
        builder.ToTable("ConsultationBookings");
        builder.HasKey(b => b.Id);
        builder.Property(b => b.VisitorFullName).HasMaxLength(200).IsRequired();
        builder.Property(b => b.VisitorMobileNumber).HasMaxLength(15).IsRequired();
        builder.Property(b => b.Notes).HasMaxLength(1000);
        builder.Property(b => b.PriceSnapshotIrr).HasPrecision(18, 0);
        builder.HasOne(b => b.Consultant)
            .WithMany()
            .HasForeignKey(b => b.ConsultantUserId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(b => b.User)
            .WithMany()
            .HasForeignKey(b => b.UserId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(b => new { b.ConsultantUserId, b.StartAtUtc, b.EndAtUtc, b.Status });
    }
}

public class LearningCertificateConfiguration : IEntityTypeConfiguration<LearningCertificate>
{
    public void Configure(EntityTypeBuilder<LearningCertificate> builder)
    {
        builder.ToTable("LearningCertificates");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.CertificateNumber).HasMaxLength(64).IsRequired();
        builder.Property(c => c.VerificationToken).HasMaxLength(128).IsRequired();
        builder.Property(c => c.StudentFullNameSnapshot).HasMaxLength(200).IsRequired();
        builder.Property(c => c.TeacherFullNameSnapshot).HasMaxLength(200).IsRequired();
        builder.Property(c => c.InstrumentNameSnapshot).HasMaxLength(120).IsRequired();
        builder.Property(c => c.CourseTitleSnapshot).HasMaxLength(200).IsRequired();
        builder.HasIndex(c => c.CertificateNumber).IsUnique();
        builder.HasIndex(c => c.VerificationToken).IsUnique();
    }
}
