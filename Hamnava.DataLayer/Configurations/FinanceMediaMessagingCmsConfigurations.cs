using Hamnava.Domain.Entities.Audit;
using Hamnava.Domain.Entities.Cms;
using Hamnava.Domain.Entities.Finance;
using Hamnava.Domain.Entities.Media;
using Hamnava.Domain.Entities.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Hamnava.DataLayer.Configurations;

public class FinancialSettingConfiguration : IEntityTypeConfiguration<FinancialSetting>
{
    public void Configure(EntityTypeBuilder<FinancialSetting> builder)
    {
        builder.ToTable("FinancialSettings");
        builder.HasKey(f => f.Id);
        builder.Property(f => f.DefaultTeacherCommissionPercentage).HasPrecision(5, 2);
        builder.Property(f => f.DefaultAcademyCommissionPercentage).HasPrecision(5, 2);
        builder.Property(f => f.DefaultReferrerCommissionValue).HasPrecision(18, 2);
        builder.Property(f => f.StudentReferralRewardValue).HasPrecision(18, 2);
        builder.Property(f => f.MinimumWithdrawalAmountIrr).HasPrecision(18, 0);
        builder.HasIndex(f => f.IsCurrent);
    }
}

public class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> builder)
    {
        builder.ToTable("Payments", t =>
            t.HasCheckConstraint("CK_Payments_FinalAmount_NonNegative", "[FinalAmountIrr] >= 0"));
        builder.HasKey(p => p.Id);
        builder.Property(p => p.OriginalAmountIrr).HasPrecision(18, 0);
        builder.Property(p => p.DiscountAmountIrr).HasPrecision(18, 0);
        builder.Property(p => p.FinalAmountIrr).HasPrecision(18, 0);
        builder.Property(p => p.GatewayName).HasMaxLength(50).IsRequired();
        builder.Property(p => p.GatewayAuthority).HasMaxLength(128);
        builder.Property(p => p.GatewayRefId).HasMaxLength(128);
        builder.Property(p => p.GatewayCardPanMasked).HasMaxLength(32);
        builder.Property(p => p.GatewayStatusCode).HasMaxLength(32);
        builder.Property(p => p.Description).HasMaxLength(500).IsRequired();
        builder.Property(p => p.IdempotencyKey).HasMaxLength(128).IsRequired();

        builder.HasIndex(p => p.IdempotencyKey).IsUnique();
        builder.HasIndex(p => p.GatewayAuthority).IsUnique().HasFilter("[GatewayAuthority] IS NOT NULL");
        builder.HasIndex(p => new { p.UserId, p.Status, p.CreatedAtUtc });
        builder.HasIndex(p => new { p.PaymentType, p.ReferenceEntityId });
    }
}

public class PaymentTransactionConfiguration : IEntityTypeConfiguration<PaymentTransaction>
{
    public void Configure(EntityTypeBuilder<PaymentTransaction> builder)
    {
        builder.ToTable("PaymentTransactions");
        builder.HasKey(t => t.Id);
        builder.Property(t => t.TransactionType).HasMaxLength(50).IsRequired();
        builder.Property(t => t.Authority).HasMaxLength(128);
        builder.Property(t => t.RefId).HasMaxLength(128);
        builder.Property(t => t.VerifiedAmountIrr).HasPrecision(18, 0);
        builder.HasIndex(t => t.PaymentId);
    }
}

public class DiscountCodeConfiguration : IEntityTypeConfiguration<DiscountCode>
{
    public void Configure(EntityTypeBuilder<DiscountCode> builder)
    {
        builder.ToTable("DiscountCodes");
        builder.HasKey(d => d.Id);
        builder.Property(d => d.Code).HasMaxLength(64).IsRequired();
        builder.Property(d => d.Title).HasMaxLength(200).IsRequired();
        builder.Property(d => d.DiscountValue).HasPrecision(18, 2);
        builder.Property(d => d.MaxDiscountAmountIrr).HasPrecision(18, 0);
        builder.Property(d => d.MinimumPayableAmountIrr).HasPrecision(18, 0);
        builder.Property(d => d.RewardCycleKey).HasMaxLength(128);

        builder.HasIndex(d => d.Code).IsUnique();
        builder.HasIndex(d => d.RewardCycleKey).IsUnique().HasFilter("[RewardCycleKey] IS NOT NULL");
        builder.HasIndex(d => new { d.IsActive, d.ExpiresAtUtc });
    }
}

public class DiscountCodeEligiblePlanConfiguration : IEntityTypeConfiguration<DiscountCodeEligiblePlan>
{
    public void Configure(EntityTypeBuilder<DiscountCodeEligiblePlan> builder)
    {
        builder.ToTable("DiscountCodeEligiblePlans");
        builder.HasKey(dp => dp.Id);
        builder.HasIndex(dp => new { dp.DiscountCodeId, dp.CoursePlanId }).IsUnique();
    }
}

public class DiscountUsageConfiguration : IEntityTypeConfiguration<DiscountUsage>
{
    public void Configure(EntityTypeBuilder<DiscountUsage> builder)
    {
        builder.ToTable("DiscountUsages");
        builder.HasKey(u => u.Id);
        builder.Property(u => u.DiscountAppliedIrr).HasPrecision(18, 0);
        builder.HasIndex(u => new { u.DiscountCodeId, u.UserId });
        builder.HasIndex(u => u.PaymentId).IsUnique();
    }
}

public class WalletConfiguration : IEntityTypeConfiguration<Wallet>
{
    public void Configure(EntityTypeBuilder<Wallet> builder)
    {
        builder.ToTable("Wallets");
        builder.HasKey(w => w.Id);
        builder.Property(w => w.AvailableBalanceIrr).HasPrecision(18, 0);
        builder.Property(w => w.PendingBalanceIrr).HasPrecision(18, 0);
        builder.Property(w => w.TotalEarnedIrr).HasPrecision(18, 0);
        builder.Property(w => w.TotalWithdrawnIrr).HasPrecision(18, 0);
        builder.HasIndex(w => w.UserId).IsUnique();
    }
}

public class WalletLedgerConfiguration : IEntityTypeConfiguration<WalletLedger>
{
    public void Configure(EntityTypeBuilder<WalletLedger> builder)
    {
        builder.ToTable("WalletLedgers");
        builder.HasKey(l => l.Id);
        builder.Property(l => l.AmountIrr).HasPrecision(18, 0);
        builder.Property(l => l.BalanceAfterIrr).HasPrecision(18, 0);
        builder.Property(l => l.AppliedCommissionPercentageSnapshot).HasPrecision(5, 2);
        builder.Property(l => l.CalculationBaseAmountIrrSnapshot).HasPrecision(18, 0);
        builder.Property(l => l.Description).HasMaxLength(500).IsRequired();
        builder.Property(l => l.IdempotencyKey).HasMaxLength(150).IsRequired();

        // Guarantees zero duplicate session/referral credits even under concurrent retries
        builder.HasIndex(l => l.IdempotencyKey).IsUnique();
        builder.HasIndex(l => new { l.WalletId, l.CreatedAtUtc });
        builder.HasIndex(l => new { l.BeneficiaryUserId, l.EntryType });
    }
}

public class WalletWithdrawalConfiguration : IEntityTypeConfiguration<WalletWithdrawal>
{
    public void Configure(EntityTypeBuilder<WalletWithdrawal> builder)
    {
        builder.ToTable("WalletWithdrawals", t =>
            t.HasCheckConstraint("CK_WalletWithdrawals_Amount_Positive", "[RequestedAmountIrr] > 0"));
        builder.HasKey(w => w.Id);
        builder.Property(w => w.RequestedAmountIrr).HasPrecision(18, 0);
        builder.Property(w => w.DestinationSheba).HasMaxLength(34);
        builder.Property(w => w.DestinationCardMasked).HasMaxLength(32);
        builder.Property(w => w.ReviewNotes).HasMaxLength(500);
        builder.Property(w => w.BankReferenceNumber).HasMaxLength(100);
        builder.HasIndex(w => new { w.BeneficiaryUserId, w.Status });
    }
}

public class TeacherSettlementConfiguration : IEntityTypeConfiguration<TeacherSettlement>
{
    public void Configure(EntityTypeBuilder<TeacherSettlement> builder)
    {
        builder.ToTable("TeacherSettlements");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.SettledAmountIrr).HasPrecision(18, 0);
        builder.Property(s => s.BankReferenceNumber).HasMaxLength(100).IsRequired();
        builder.Property(s => s.Notes).HasMaxLength(500);
    }
}

public class ReferralRewardConfiguration : IEntityTypeConfiguration<ReferralReward>
{
    public void Configure(EntityTypeBuilder<ReferralReward> builder)
    {
        builder.ToTable("ReferralRewards");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.RewardValue).HasPrecision(18, 2);

        // Guarantees the first-course referral reward is granted EXACTLY ONCE per invited student
        builder.HasIndex(r => r.InvitedStudentUserId).IsUnique();
        builder.HasIndex(r => r.FirstSuccessfulPaymentId).IsUnique();
        builder.HasIndex(r => r.ReferringUserId);
    }
}

public class PortfolioMediaConfiguration : IEntityTypeConfiguration<PortfolioMedia>
{
    public void Configure(EntityTypeBuilder<PortfolioMedia> builder)
    {
        builder.ToTable("PortfolioMedias");
        builder.HasKey(m => m.Id);
        builder.Property(m => m.Title).HasMaxLength(200).IsRequired();
        builder.Property(m => m.Description).HasMaxLength(1000);
        builder.Property(m => m.OriginalStorageKey).HasMaxLength(500).IsRequired();
        builder.Property(m => m.OptimizedStorageKey).HasMaxLength(500);
        builder.Property(m => m.ThumbnailStorageKey).HasMaxLength(500);
        builder.Property(m => m.OriginalFileNameSanitized).HasMaxLength(260).IsRequired();
        builder.Property(m => m.ContentType).HasMaxLength(100).IsRequired();
        builder.HasIndex(m => new { m.StudentUserId, m.IsPubliclyVisible, m.ProcessingStatus, m.CreatedAtUtc });
    }
}

public class MediaConsentConfiguration : IEntityTypeConfiguration<MediaConsent>
{
    public void Configure(EntityTypeBuilder<MediaConsent> builder)
    {
        builder.ToTable("MediaConsents");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.GuardianNameIfMinor).HasMaxLength(200);
        builder.Property(c => c.ConsentIpAddress).HasMaxLength(64);
        builder.HasIndex(c => c.StudentUserId).IsUnique();
    }
}

public class SmsTemplateConfiguration : IEntityTypeConfiguration<SmsTemplate>
{
    public void Configure(EntityTypeBuilder<SmsTemplate> builder)
    {
        builder.ToTable("SmsTemplates");
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Code).HasMaxLength(80).IsRequired();
        builder.Property(t => t.Title).HasMaxLength(200).IsRequired();
        builder.Property(t => t.BodyTemplate).HasMaxLength(1000).IsRequired();
        builder.Property(t => t.AllowedPlaceholdersCsv).HasMaxLength(500);
        builder.Property(t => t.ProviderPatternCode).HasMaxLength(100);
        builder.HasIndex(t => t.Code).IsUnique();
        builder.HasIndex(t => new { t.TriggerType, t.IsActive });
    }
}

public class SmsHistoryConfiguration : IEntityTypeConfiguration<SmsHistory>
{
    public void Configure(EntityTypeBuilder<SmsHistory> builder)
    {
        builder.ToTable("SmsHistories");
        builder.HasKey(h => h.Id);
        builder.Property(h => h.RecipientMobileNumber).HasMaxLength(15).IsRequired();
        builder.Property(h => h.ProtectedMessageContent).HasMaxLength(1000).IsRequired();
        builder.Property(h => h.ProviderName).HasMaxLength(80).IsRequired();
        builder.Property(h => h.ProviderMessageId).HasMaxLength(128);
        builder.Property(h => h.IdempotencyKey).HasMaxLength(150);
        builder.HasIndex(h => h.IdempotencyKey).IsUnique().HasFilter("[IdempotencyKey] IS NOT NULL");
        builder.HasIndex(h => new { h.RecipientMobileNumber, h.RequestedAtUtc });
        builder.HasIndex(h => new { h.DeliveryStatus, h.RequestedAtUtc });
    }
}

public class SmsCampaignConfiguration : IEntityTypeConfiguration<SmsCampaign>
{
    public void Configure(EntityTypeBuilder<SmsCampaign> builder)
    {
        builder.ToTable("SmsCampaigns");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Title).HasMaxLength(200).IsRequired();
        builder.Property(c => c.MessageBody).HasMaxLength(1000).IsRequired();
        builder.Property(c => c.IdempotencyKey).HasMaxLength(150).IsRequired();
        builder.HasIndex(c => c.IdempotencyKey).IsUnique();
    }
}

public class SystemSettingConfiguration : IEntityTypeConfiguration<SystemSetting>
{
    public void Configure(EntityTypeBuilder<SystemSetting> builder)
    {
        builder.ToTable("SystemSettings");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Key).HasMaxLength(150).IsRequired();
        builder.Property(s => s.Value).HasMaxLength(2000).IsRequired();
        builder.Property(s => s.Category).HasMaxLength(80).IsRequired();
        builder.Property(s => s.DataType).HasMaxLength(40).IsRequired();
        builder.Property(s => s.DisplayLabelFa).HasMaxLength(200).IsRequired();
        builder.Property(s => s.DescriptionFa).HasMaxLength(500);
        builder.HasIndex(s => s.Key).IsUnique();
    }
}

public class WebsitePageConfiguration : IEntityTypeConfiguration<WebsitePage>
{
    public void Configure(EntityTypeBuilder<WebsitePage> builder)
    {
        builder.ToTable("WebsitePages");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Slug).HasMaxLength(150).IsRequired();
        builder.Property(p => p.Title).HasMaxLength(200).IsRequired();
        builder.Property(p => p.MetaTitle).HasMaxLength(200).IsRequired();
        builder.Property(p => p.MetaDescription).HasMaxLength(500).IsRequired();
        builder.HasIndex(p => p.Slug).IsUnique();
    }
}

public class PublicArticleConfiguration : IEntityTypeConfiguration<PublicArticle>
{
    public void Configure(EntityTypeBuilder<PublicArticle> builder)
    {
        builder.ToTable("PublicArticles");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Slug).HasMaxLength(180).IsRequired();
        builder.Property(a => a.Title).HasMaxLength(250).IsRequired();
        builder.Property(a => a.Summary).HasMaxLength(600).IsRequired();
        builder.HasIndex(a => a.Slug).IsUnique();
        builder.HasIndex(a => new { a.IsPublished, a.PublishedAtUtc });
    }
}

public class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> builder)
    {
        builder.ToTable("AuditLogs");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Action).HasMaxLength(120).IsRequired();
        builder.Property(a => a.EntityName).HasMaxLength(120).IsRequired();
        builder.Property(a => a.EntityId).HasMaxLength(100);
        builder.Property(a => a.CorrelationId).HasMaxLength(100);
        builder.Property(a => a.IpAddress).HasMaxLength(64);
        builder.HasIndex(a => new { a.EntityName, a.EntityId, a.OccurredAtUtc });
        builder.HasIndex(a => new { a.ActorUserId, a.OccurredAtUtc });
    }
}

public class IdempotencyRecordConfiguration : IEntityTypeConfiguration<IdempotencyRecord>
{
    public void Configure(EntityTypeBuilder<IdempotencyRecord> builder)
    {
        builder.ToTable("IdempotencyRecords");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Key).HasMaxLength(150).IsRequired();
        builder.Property(r => r.Scope).HasMaxLength(80).IsRequired();
        builder.Property(r => r.RequestHash).HasMaxLength(128).IsRequired();
        builder.HasIndex(r => new { r.Scope, r.Key }).IsUnique();
        builder.HasIndex(r => r.ExpiresAtUtc);
    }
}
