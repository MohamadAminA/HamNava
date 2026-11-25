using Hamnava.Domain.Entities.Identity;
using Hamnava.Domain.Enums;

namespace Hamnava.Domain.Entities.Finance;

/// <summary>
/// Normalized, versioned financial policy table editable via Admin panel.
/// </summary>
public class FinancialSetting : AuditableSoftDeleteEntity
{
    public decimal DefaultTeacherCommissionPercentage { get; set; } = 60m;
    public decimal DefaultAcademyCommissionPercentage { get; set; } = 40m;

    public ReferrerCommissionType DefaultReferrerCommissionType { get; set; } = ReferrerCommissionType.Percentage;
    public decimal DefaultReferrerCommissionValue { get; set; } = 5m;

    /// <summary>If true, referrer share is deducted from academy share; otherwise checked against 100% total.</summary>
    public bool DeductReferrerShareFromAcademyShare { get; set; } = true;

    public CommissionCalculationBasis CalculationBasis { get; set; } = CommissionCalculationBasis.PostDiscountFinalAmount;
    public FinancialRoundingMode RoundingMode { get; set; } = FinancialRoundingMode.AwayFromZeroToNearest1000Irr;

    // Initial SRS referral reward rule: configurable 50% discount for referring student after invitee's 1st paid course
    public ReferralRewardType StudentReferralRewardType { get; set; } = ReferralRewardType.DiscountPercentage;
    public decimal StudentReferralRewardValue { get; set; } = 50m;
    public int StudentReferralRewardValidityDays { get; set; } = 90;

    // Attendance financial settlement policy
    public bool SettleCommissionOnUnauthorizedAbsence { get; set; } = true;
    public bool SettleCommissionOnAuthorizedAbsence { get; set; } = false;

    // Wallet & withdrawal policy
    public decimal MinimumWithdrawalAmountIrr { get; set; } = 1_000_000m;
    public bool AllowNegativeWalletBalance { get; set; } = false;

    public bool IsCurrent { get; set; } = true;
}

public class Payment : AuditableSoftDeleteEntity
{
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;

    public PaymentType PaymentType { get; set; }
    public PaymentStatus Status { get; set; } = PaymentStatus.Pending;

    /// <summary>ID of Enrollment, InstrumentRental, InstrumentRentalRenewal, or ConsultationBooking.</summary>
    public Guid ReferenceEntityId { get; set; }

    public decimal OriginalAmountIrr { get; set; }
    public decimal DiscountAmountIrr { get; set; }
    public decimal FinalAmountIrr { get; set; }

    public Guid? DiscountCodeId { get; set; }
    public DiscountCode? DiscountCode { get; set; }

    public string GatewayName { get; set; } = "Zarinpal";
    public string? GatewayAuthority { get; set; }
    public string? GatewayRefId { get; set; }
    public string? GatewayCardPanMasked { get; set; }
    public string? GatewayStatusCode { get; set; }
    public string Description { get; set; } = string.Empty;

    public DateTime RequestedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? VerifiedAtUtc { get; set; }
    public DateTime ExpiresAtUtc { get; set; }

    /// <summary>Guarantees business fulfillment executes exactly once across duplicate callbacks.</summary>
    public bool IsFulfilled { get; set; }
    public DateTime? FulfilledAtUtc { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;

    public ICollection<PaymentAttempt> Attempts { get; set; } = new List<PaymentAttempt>();
    public ICollection<PaymentTransaction> Transactions { get; set; } = new List<PaymentTransaction>();
}

public class PaymentAttempt : BaseEntity
{
    public Guid PaymentId { get; set; }
    public Payment Payment { get; set; } = null!;

    public string GatewayName { get; set; } = "Zarinpal";
    public string? Authority { get; set; }
    public string? RequestPayloadRedacted { get; set; }
    public string? ResponsePayloadRedacted { get; set; }
    public int? HttpStatusCode { get; set; }
    public bool IsSuccessful { get; set; }
    public string? ErrorMessage { get; set; }
}

public class PaymentTransaction : BaseEntity
{
    public Guid PaymentId { get; set; }
    public Payment Payment { get; set; } = null!;

    public string TransactionType { get; set; } = "Verify";
    public string? Authority { get; set; }
    public string? RefId { get; set; }
    public int GatewayCode { get; set; }
    public decimal VerifiedAmountIrr { get; set; }
    public string? RawStatus { get; set; }
    public DateTime ProcessedAtUtc { get; set; } = DateTime.UtcNow;
}

public class DiscountCode : AuditableSoftDeleteEntity
{
    public string Code { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public DiscountScope Scope { get; set; } = DiscountScope.SystemWide;
    public DiscountType DiscountType { get; set; } = DiscountType.Percentage;
    public decimal DiscountValue { get; set; }
    public decimal? MaxDiscountAmountIrr { get; set; }
    public decimal? MinimumPayableAmountIrr { get; set; }

    public int? MaxTotalUsages { get; set; }
    public int CurrentUsageCount { get; set; }
    public int MaxUsagesPerUser { get; set; } = 1;

    public DateTime StartsAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? ExpiresAtUtc { get; set; }
    public bool IsActive { get; set; } = true;

    /// <summary>When Scope == TeacherSpecific, enforces ownership restriction to this teacher's students only.</summary>
    public Guid? OwnerTeacherUserId { get; set; }
    public User? OwnerTeacher { get; set; }

    /// <summary>When bound to a specific beneficiary (e.g., ReferralReward or BirthdayReward).</summary>
    public Guid? RestrictedToUserId { get; set; }
    public User? RestrictedToUser { get; set; }

    /// <summary>Prevents duplicate birthday reward issuance for the same user + Jalali year cycle.</summary>
    public string? RewardCycleKey { get; set; }

    public ICollection<DiscountCodeEligiblePlan> EligiblePlans { get; set; } = new List<DiscountCodeEligiblePlan>();
    public ICollection<DiscountUsage> Usages { get; set; } = new List<DiscountUsage>();
}

public class DiscountCodeEligiblePlan : BaseEntity
{
    public Guid DiscountCodeId { get; set; }
    public DiscountCode DiscountCode { get; set; } = null!;

    public Guid CoursePlanId { get; set; }
    public Education.CoursePlan CoursePlan { get; set; } = null!;
}

public class DiscountUsage : BaseEntity
{
    public Guid DiscountCodeId { get; set; }
    public DiscountCode DiscountCode { get; set; } = null!;

    public Guid UserId { get; set; }
    public User User { get; set; } = null!;

    public Guid PaymentId { get; set; }
    public Payment Payment { get; set; } = null!;

    public decimal DiscountAppliedIrr { get; set; }
    public DateTime UsedAtUtc { get; set; } = DateTime.UtcNow;
}

public class Wallet : BaseEntity
{
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;

    /// <summary>Transactionally synchronized balance in IRR; WalletLedgers is the authoritative immutable audit trail.</summary>
    public decimal AvailableBalanceIrr { get; set; }
    public decimal PendingBalanceIrr { get; set; }
    public decimal TotalEarnedIrr { get; set; }
    public decimal TotalWithdrawnIrr { get; set; }

    public ICollection<WalletLedger> LedgerEntries { get; set; } = new List<WalletLedger>();
    public ICollection<WalletWithdrawal> Withdrawals { get; set; } = new List<WalletWithdrawal>();
}

/// <summary>
/// Immutable append-only financial ledger. Never update or delete historical rows;
/// post compensating Reversal or AdjustmentDebit/Credit entries instead.
/// </summary>
public class WalletLedger : BaseEntity
{
    public Guid WalletId { get; set; }
    public Wallet Wallet { get; set; } = null!;

    public Guid BeneficiaryUserId { get; set; }
    public User BeneficiaryUser { get; set; } = null!;

    public WalletLedgerEntryType EntryType { get; set; }

    /// <summary>Signed amount in IRR (+ for credits, - for debits/withdrawals).</summary>
    public decimal AmountIrr { get; set; }
    public decimal BalanceAfterIrr { get; set; }

    public Guid? RelatedSessionId { get; set; }
    public Guid? RelatedPaymentId { get; set; }
    public Guid? RelatedStudentUserId { get; set; }
    public Guid? RelatedWithdrawalId { get; set; }
    public Guid? ReversedLedgerId { get; set; }

    /// <summary>Snapshot of commission rate/basis used when this ledger entry was posted.</summary>
    public decimal? AppliedCommissionPercentageSnapshot { get; set; }
    public decimal? CalculationBaseAmountIrrSnapshot { get; set; }

    public string Description { get; set; } = string.Empty;

    /// <summary>Unique index enforcing strict idempotency (e.g. "SESSION:{SessionId}:TEACHER:{UserId}").</summary>
    public string IdempotencyKey { get; set; } = string.Empty;

    public Guid? CreatedByUserId { get; set; }
}

public class WalletWithdrawal : BaseEntity
{
    public Guid WalletId { get; set; }
    public Wallet Wallet { get; set; } = null!;

    public Guid BeneficiaryUserId { get; set; }
    public User BeneficiaryUser { get; set; } = null!;

    public decimal RequestedAmountIrr { get; set; }
    public string? DestinationSheba { get; set; }
    public string? DestinationCardMasked { get; set; }

    public WithdrawalStatus Status { get; set; } = WithdrawalStatus.Requested;
    public Guid? ReviewedByUserId { get; set; }
    public DateTime? ReviewedAtUtc { get; set; }
    public string? ReviewNotes { get; set; }

    public DateTime? PaidAtUtc { get; set; }
    public string? BankReferenceNumber { get; set; }
}

public class TeacherSettlement : BaseEntity
{
    public Guid TeacherUserId { get; set; }
    public User Teacher { get; set; } = null!;

    public Guid? WalletWithdrawalId { get; set; }
    public WalletWithdrawal? WalletWithdrawal { get; set; }

    public decimal SettledAmountIrr { get; set; }
    public DateOnly PeriodStartGregorian { get; set; }
    public DateOnly PeriodEndGregorian { get; set; }
    public string BankReferenceNumber { get; set; } = string.Empty;
    public string? Notes { get; set; }
    public Guid SettledByAdminUserId { get; set; }
    public DateTime SettledAtUtc { get; set; } = DateTime.UtcNow;
}

public class ReferralReward : BaseEntity
{
    public Guid ReferringUserId { get; set; }
    public User ReferringUser { get; set; } = null!;

    public Guid InvitedStudentUserId { get; set; }
    public User InvitedStudent { get; set; } = null!;

    public Guid FirstSuccessfulPaymentId { get; set; }
    public Payment FirstSuccessfulPayment { get; set; } = null!;

    public ReferralRewardType RewardType { get; set; }
    public decimal RewardValue { get; set; }

    public Guid? GeneratedDiscountCodeId { get; set; }
    public DiscountCode? GeneratedDiscountCode { get; set; }

    public Guid? WalletLedgerId { get; set; }

    public DateTime? ExpiresAtUtc { get; set; }
    public bool IsRedeemed { get; set; }
    public DateTime? RedeemedAtUtc { get; set; }
}
