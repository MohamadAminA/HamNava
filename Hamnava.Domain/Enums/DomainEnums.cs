namespace Hamnava.Domain.Enums;

public enum UserStatus
{
    PendingVerification = 0,
    Active = 1,
    Inactive = 2,
    Suspended = 3
}

public enum ConsentType
{
    TermsOfService = 1,
    PublicPortfolioVisibility = 2,
    PublicMediaPublication = 3,
    SmsNotifications = 4
}

public enum EnrollmentStatus
{
    PendingPayment = 0,
    Active = 1,
    Completed = 2,
    Paused = 3,
    Canceled = 4,
    Expired = 5
}

public enum ClassSessionStatus
{
    Scheduled = 0,
    Present = 1,
    AuthorizedAbsence = 2,
    UnauthorizedAbsence = 3,
    Holiday = 4,
    Rescheduled = 5,
    Canceled = 6
}

public enum ClassSessionType
{
    Regular = 0,
    Makeup = 1,
    Rescheduled = 2,
    Trial = 3
}

public enum AvailabilitySlotType
{
    Both = 0,
    RescheduleOnly = 1,
    MakeupOnly = 2,
    Consultation = 3
}

public enum RequestWorkflowStatus
{
    Pending = 0,
    Approved = 1,
    Rejected = 2,
    Canceled = 3,
    Completed = 4
}

public enum QuotaUsageType
{
    AuthorizedAbsence = 1,
    RescheduleRequest = 2,
    MakeupSelection = 3,
    AdministrativeReset = 4,
    Reversal = 5
}

public enum PaymentType
{
    Tuition = 1,
    InstrumentRental = 2,
    Consultation = 3,
    RentalRenewal = 4
}

public enum PaymentStatus
{
    Pending = 0,
    Redirected = 1,
    Verified = 2,
    Failed = 3,
    Canceled = 4,
    Expired = 5,
    Refunded = 6
}

public enum DiscountType
{
    Percentage = 1,
    FixedAmountIrr = 2
}

public enum DiscountScope
{
    SystemWide = 1,
    TeacherSpecific = 2,
    ReferralReward = 3,
    BirthdayReward = 4
}

public enum ReferrerCommissionType
{
    Percentage = 1,
    FixedAmountIrr = 2
}

public enum CommissionCalculationBasis
{
    PostDiscountFinalAmount = 1,
    PreDiscountOriginalAmount = 2
}

public enum FinancialRoundingMode
{
    AwayFromZeroToNearest1000Irr = 1,
    FloorTo1000Irr = 2,
    ExactIntegerIrr = 3
}

public enum WalletLedgerEntryType
{
    SessionCredit = 1,
    ReferralCredit = 2,
    AdjustmentCredit = 3,
    AdjustmentDebit = 4,
    SettlementWithdrawal = 5,
    Reversal = 6
}

public enum WithdrawalStatus
{
    Requested = 0,
    Approved = 1,
    Rejected = 2,
    Paid = 3,
    Canceled = 4
}

public enum ReferralRewardType
{
    DiscountPercentage = 1,
    DiscountFixedIrr = 2,
    WalletCreditIrr = 3
}

public enum RentalStatus
{
    PendingPayment = 0,
    Active = 1,
    Extended = 2,
    Returned = 3,
    Canceled = 4,
    Expired = 5
}

public enum RentalPricingUnit
{
    Daily = 1,
    Monthly = 2,
    CustomDays = 3
}

public enum ConsultationBookingStatus
{
    PendingPayment = 0,
    Confirmed = 1,
    Canceled = 2,
    Completed = 3,
    Refunded = 4
}

public enum MediaType
{
    Image = 1,
    Video = 2
}

public enum MediaProcessingStatus
{
    Pending = 0,
    Uploaded = 1,
    Processing = 2,
    Completed = 3,
    Failed = 4,
    Deleted = 5
}

public enum SmsTriggerType
{
    Manual = 0,
    OtpVerification = 1,
    EnrollmentConfirmed = 2,
    PaymentConfirmed = 3,
    AuthorizedAbsenceConfirmed = 4,
    RescheduleConfirmed = 5,
    MakeupConfirmed = 6,
    LessonReminder = 7,
    BirthdayGreeting = 8,
    DiscountIssued = 9,
    RentalExpirationReminder = 10,
    RentalRenewalConfirmed = 11,
    ReferralRewardGranted = 12,
    ConsultationConfirmed = 13,
    AdminAnnouncement = 14
}

public enum SmsDeliveryStatus
{
    Queued = 0,
    Submitted = 1,
    AcceptedByProvider = 2,
    Delivered = 3,
    Failed = 4,
    Unknown = 5
}

public enum SmsCampaignStatus
{
    Draft = 0,
    Queued = 1,
    Processing = 2,
    Completed = 3,
    PartiallyFailed = 4,
    Failed = 5
}
