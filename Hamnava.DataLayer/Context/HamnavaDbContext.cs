using System.Linq.Expressions;
using Hamnava.Domain.Entities;
using Hamnava.Domain.Entities.Audit;
using Hamnava.Domain.Entities.Cms;
using Hamnava.Domain.Entities.Education;
using Hamnava.Domain.Entities.Finance;
using Hamnava.Domain.Entities.Identity;
using Hamnava.Domain.Entities.Instruments;
using Hamnava.Domain.Entities.Media;
using Hamnava.Domain.Entities.Messaging;
using Hamnava.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Hamnava.DataLayer.Context;

public class HamnavaDbContext : DbContext
{
    public HamnavaDbContext(DbContextOptions<HamnavaDbContext> options) : base(options)
    {
    }

    // Identity & Access
    public DbSet<User> Users => Set<User>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<UserRole> UserRoles => Set<UserRole>();
    public DbSet<TeacherInstrument> TeacherInstruments => Set<TeacherInstrument>();
    public DbSet<TeacherStudentAssignment> TeacherStudentAssignments => Set<TeacherStudentAssignment>();
    public DbSet<OtpRequest> OtpRequests => Set<OtpRequest>();
    public DbSet<UserSession> UserSessions => Set<UserSession>();
    public DbSet<UserConsent> UserConsents => Set<UserConsent>();

    // Instruments & Classrooms
    public DbSet<InstrumentCategory> InstrumentCategories => Set<InstrumentCategory>();
    public DbSet<Instrument> Instruments => Set<Instrument>();
    public DbSet<InstrumentRental> InstrumentRentals => Set<InstrumentRental>();
    public DbSet<InstrumentRentalRenewal> InstrumentRentalRenewals => Set<InstrumentRentalRenewal>();
    public DbSet<Room> Rooms => Set<Room>();
    public DbSet<RoomSupportedInstrument> RoomSupportedInstruments => Set<RoomSupportedInstrument>();

    // Education, Scheduling, Attendance & Quota
    public DbSet<CoursePlan> CoursePlans => Set<CoursePlan>();
    public DbSet<Enrollment> Enrollments => Set<Enrollment>();
    public DbSet<ClassSession> ClassSessions => Set<ClassSession>();
    public DbSet<SessionScheduleHistory> SessionScheduleHistories => Set<SessionScheduleHistory>();
    public DbSet<TeacherTimeSlot> TeacherTimeSlots => Set<TeacherTimeSlot>();
    public DbSet<StudentQuotaPeriod> StudentQuotaPeriods => Set<StudentQuotaPeriod>();
    public DbSet<StudentQuotaUsage> StudentQuotaUsages => Set<StudentQuotaUsage>();
    public DbSet<AttendanceRecord> AttendanceRecords => Set<AttendanceRecord>();
    public DbSet<MakeupLessonRequest> MakeupLessonRequests => Set<MakeupLessonRequest>();
    public DbSet<RescheduleRequest> RescheduleRequests => Set<RescheduleRequest>();
    public DbSet<PracticeNote> PracticeNotes => Set<PracticeNote>();
    public DbSet<PracticeAssignment> PracticeAssignments => Set<PracticeAssignment>();
    public DbSet<AcademicHoliday> AcademicHolidays => Set<AcademicHoliday>();
    public DbSet<ConsultationPlan> ConsultationPlans => Set<ConsultationPlan>();
    public DbSet<ConsultationPlanConsultant> ConsultationPlanConsultants => Set<ConsultationPlanConsultant>();
    public DbSet<ConsultationBooking> ConsultationBookings => Set<ConsultationBooking>();
    public DbSet<LearningCertificate> LearningCertificates => Set<LearningCertificate>();

    // Finance, Wallets, Discounts & Referrals
    public DbSet<FinancialSetting> FinancialSettings => Set<FinancialSetting>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<PaymentAttempt> PaymentAttempts => Set<PaymentAttempt>();
    public DbSet<PaymentTransaction> PaymentTransactions => Set<PaymentTransaction>();
    public DbSet<DiscountCode> DiscountCodes => Set<DiscountCode>();
    public DbSet<DiscountCodeEligiblePlan> DiscountCodeEligiblePlans => Set<DiscountCodeEligiblePlan>();
    public DbSet<DiscountUsage> DiscountUsages => Set<DiscountUsage>();
    public DbSet<Wallet> Wallets => Set<Wallet>();
    public DbSet<WalletLedger> WalletLedgers => Set<WalletLedger>();
    public DbSet<WalletWithdrawal> WalletWithdrawals => Set<WalletWithdrawal>();
    public DbSet<TeacherSettlement> TeacherSettlements => Set<TeacherSettlement>();
    public DbSet<ReferralReward> ReferralRewards => Set<ReferralReward>();

    // Portfolio & Media
    public DbSet<PortfolioMedia> PortfolioMedias => Set<PortfolioMedia>();
    public DbSet<MediaProcessingJob> MediaProcessingJobs => Set<MediaProcessingJob>();
    public DbSet<MediaConsent> MediaConsents => Set<MediaConsent>();
    public DbSet<PortfolioView> PortfolioViews => Set<PortfolioView>();
    public DbSet<StoryTemplate> StoryTemplates => Set<StoryTemplate>();

    // Messaging
    public DbSet<SmsTemplate> SmsTemplates => Set<SmsTemplate>();
    public DbSet<SmsHistory> SmsHistories => Set<SmsHistory>();
    public DbSet<SmsCampaign> SmsCampaigns => Set<SmsCampaign>();
    public DbSet<SmsRecipient> SmsRecipients => Set<SmsRecipient>();

    // CMS & Settings
    public DbSet<SystemSetting> SystemSettings => Set<SystemSetting>();
    public DbSet<WebsitePage> WebsitePages => Set<WebsitePage>();
    public DbSet<WebsiteSection> WebsiteSections => Set<WebsiteSection>();
    public DbSet<WebsiteMenu> WebsiteMenus => Set<WebsiteMenu>();
    public DbSet<WebsiteAsset> WebsiteAssets => Set<WebsiteAsset>();
    public DbSet<HomepageStatistic> HomepageStatistics => Set<HomepageStatistic>();
    public DbSet<HomepageFeature> HomepageFeatures => Set<HomepageFeature>();
    public DbSet<ContactInformation> ContactInformations => Set<ContactInformation>();
    public DbSet<PublicArticle> PublicArticles => Set<PublicArticle>();
    public DbSet<PublicEvent> PublicEvents => Set<PublicEvent>();
    public DbSet<PublicTestimonial> PublicTestimonials => Set<PublicTestimonial>();
    public DbSet<FaqItem> FaqItems => Set<FaqItem>();

    // Audit & Reliability
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();
    public DbSet<BackgroundJobRecord> BackgroundJobRecords => Set<BackgroundJobRecord>();
    public DbSet<SystemNotification> SystemNotifications => Set<SystemNotification>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(HamnavaDbContext).Assembly);

        // Apply universal RowVersion concurrency tokens, safe Restrict delete defaults,
        // and global soft-delete filters across all entities.
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (typeof(BaseEntity).IsAssignableFrom(entityType.ClrType))
            {
                modelBuilder.Entity(entityType.ClrType)
                    .Property(nameof(BaseEntity.RowVersion))
                    .IsRowVersion();
            }

            if (typeof(ISoftDeletable).IsAssignableFrom(entityType.ClrType))
            {
                var parameter = Expression.Parameter(entityType.ClrType, "e");
                var prop = Expression.Property(parameter, nameof(ISoftDeletable.IsDeleted));
                var condition = Expression.Equal(prop, Expression.Constant(false));
                var lambda = Expression.Lambda(condition, parameter);
                modelBuilder.Entity(entityType.ClrType).HasQueryFilter(lambda);
            }
            else
            {
                // Define matching query filters on dependent entities with required navigations
                // to soft-deletable principals to prevent orphaned-query warnings (EF Core 10622).
                var requiredSoftDeleteNavs = entityType.GetForeignKeys()
                    .Where(fk => fk.IsRequired
                                 && fk.DependentToPrincipal is not null
                                 && typeof(ISoftDeletable).IsAssignableFrom(fk.PrincipalEntityType.ClrType))
                    .Select(fk => fk.DependentToPrincipal!.Name)
                    .Distinct()
                    .ToList();

                if (requiredSoftDeleteNavs.Count > 0)
                {
                    var parameter = Expression.Parameter(entityType.ClrType, "e");
                    Expression? combined = null;
                    foreach (var navName in requiredSoftDeleteNavs)
                    {
                        var navProp = Expression.Property(parameter, navName);
                        var isDeletedProp = Expression.Property(navProp, nameof(ISoftDeletable.IsDeleted));
                        var notDeleted = Expression.Equal(isDeletedProp, Expression.Constant(false));
                        combined = combined is null ? notDeleted : Expression.AndAlso(combined, notDeleted);
                    }

                    if (combined is not null)
                    {
                        modelBuilder.Entity(entityType.ClrType).HasQueryFilter(Expression.Lambda(combined, parameter));
                    }
                }
            }

            foreach (var fk in entityType.GetForeignKeys())
            {
                fk.DeleteBehavior = DeleteBehavior.Restrict;
            }
        }
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var utcNow = DateTime.UtcNow;
        foreach (var entry in ChangeTracker.Entries<BaseEntity>())
        {
            if (entry.State == EntityState.Added)
            {
                if (entry.Entity.CreatedAtUtc == default)
                {
                    entry.Entity.CreatedAtUtc = utcNow;
                }
            }
            else if (entry.State == EntityState.Modified)
            {
                entry.Entity.UpdatedAtUtc = utcNow;
            }
        }

        return base.SaveChangesAsync(cancellationToken);
    }
}
