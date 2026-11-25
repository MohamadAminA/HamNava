namespace Hamnava.Domain.Constants;

/// <summary>
/// Keys for structured scalar operational settings stored in SQL Server (SystemSettings).
/// Complex business domains (CoursePlans, Rooms, Instruments, FinancialSettings, SmsTemplates, etc.)
/// use dedicated normalized relational tables.
/// </summary>
public static class SystemSettingKeys
{
    // Quota Rules (initial defaults configurable via Admin panel)
    public const string QuotaPeriodDays = "Quota.PeriodDays";
    public const string QuotaMaxAuthorizedAbsences = "Quota.MaxAuthorizedAbsences";
    public const string QuotaMaxRescheduleRequests = "Quota.MaxRescheduleRequests";
    public const string QuotaMaxMakeupSelections = "Quota.MaxMakeupSelections";
    public const string QuotaMinimumAbsenceNoticeHours = "Quota.MinimumAbsenceNoticeHours";
    public const string RescheduleRequiresTeacherApproval = "Scheduling.RescheduleRequiresTeacherApproval";
    public const string HolidayAutoCreateMakeupEligibility = "Scheduling.HolidayAutoCreateMakeupEligibility";

    // OTP & Auth Security
    public const string OtpCodeLength = "Auth.Otp.Length";
    public const string OtpExpirationSeconds = "Auth.Otp.ExpirationSeconds";
    public const string OtpResendCooldownSeconds = "Auth.Otp.ResendCooldownSeconds";
    public const string OtpMaxVerificationAttempts = "Auth.Otp.MaxVerificationAttempts";
    public const string OtpMaxDailyRequestsPerMobile = "Auth.Otp.MaxDailyRequestsPerMobile";
    public const string OtpMaxRequestsPerIpPerHour = "Auth.Otp.MaxRequestsPerIpPerHour";
    public const string AuthSessionExpirationHours = "Auth.Session.ExpirationHours";

    // Birthday Discount Automation
    public const string BirthdayDiscountEnabled = "Birthday.Discount.Enabled";
    public const string BirthdayDiscountType = "Birthday.Discount.Type";
    public const string BirthdayDiscountValue = "Birthday.Discount.Value";
    public const string BirthdayDiscountMaxAmountIrr = "Birthday.Discount.MaxAmountIrr";
    public const string BirthdayDiscountValidityDays = "Birthday.Discount.ValidityDays";

    // Lesson Reminder Intervals (comma-separated hours stored as structured config, e.g. "24,2")
    public const string LessonReminderIntervalsHours = "Notifications.LessonReminderIntervalsHours";
    public const string RentalExpirationReminderDays = "Notifications.RentalExpirationReminderDays";

    // Media Upload Restrictions
    public const string MediaAllowedImageExtensions = "Media.AllowedImageExtensions";
    public const string MediaAllowedVideoExtensions = "Media.AllowedVideoExtensions";
    public const string MediaMaxImageSizeMb = "Media.MaxImageSizeMb";
    public const string MediaMaxVideoSizeMb = "Media.MaxVideoSizeMb";
    public const string MediaConvertImagesToWebP = "Media.ConvertImagesToWebP";

    // Teacher Permissions
    public const string TeacherCanCreateUnlimitedDiscounts = "Teacher.CanCreateUnlimitedDiscounts";
    public const string TeacherCanToggleStudentPublicVisibility = "Teacher.CanToggleStudentPublicVisibility";
    public const string TeacherCanResetOwnStudentQuotas = "Teacher.CanResetOwnStudentQuotas";

    // Certificate Eligibility
    public const string CertificateMinCompletedSessions = "Certificate.MinCompletedSessions";
}
