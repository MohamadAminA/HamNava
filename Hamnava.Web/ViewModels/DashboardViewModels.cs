using Hamnava.Core.Scheduling;
using Hamnava.Core.Services;
using Hamnava.Domain.Entities.Education;
using Hamnava.Domain.Entities.Finance;
using Hamnava.Domain.Entities.Identity;
using Hamnava.Domain.Entities.Instruments;
using Hamnava.Domain.Entities.Media;

namespace Hamnava.Web.ViewModels;

public sealed class AdminDashboardViewModel
{
    public string ActiveRange { get; init; } = "month";
    public string FromJalali { get; init; } = string.Empty;
    public string ToJalali { get; init; } = string.Empty;

    public int TotalActiveStudents { get; init; }
    public int TotalActiveTeachers { get; init; }
    public int TotalActiveReferrers { get; init; }
    public int ActiveEnrollmentsCount { get; init; }
    public int NewStudentRegistrations { get; init; }

    public int LessonsScheduledToday { get; init; }
    public int LessonsCompletedToday { get; init; }
    public int AuthorizedAbsencesCount { get; init; }
    public int UnauthorizedAbsencesCount { get; init; }

    public int ExpiringRentalsCount { get; init; }
    public int PendingRentalsCount { get; init; }
    public int PendingPaymentsCount { get; init; }

    public decimal TotalReceivedTuitionIrr { get; init; }
    public decimal TotalRentalRevenueIrr { get; init; }
    public decimal TotalTeacherWalletLiabilityIrr { get; init; }
    public decimal TotalReferrerRewardsIrr { get; init; }

    public IReadOnlyList<CalendarEventDto> UpcomingLessons { get; init; } = [];
    public IReadOnlyList<WalletWithdrawal> PendingWithdrawals { get; init; } = [];
}

public sealed class TeacherDashboardViewModel
{
    public string TeacherFullName { get; init; } = string.Empty;
    public int AssignedStudentsCount { get; init; }
    public int ActiveEnrollmentsCount { get; init; }
    public int PendingAttendanceCount { get; init; }
    public int UpcomingMakeupsCount { get; init; }
    public int UpcomingReschedulesCount { get; init; }
    public int PendingRentalRequestsCount { get; init; }

    public decimal AvailableWalletBalanceIrr { get; init; }
    public decimal TotalEarnedIrr { get; init; }
    public decimal TotalWithdrawnIrr { get; init; }

    public CalendarEventDto? NextClass { get; init; }
    public IReadOnlyList<CalendarEventDto> TodayClasses { get; init; } = [];
    public IReadOnlyList<WalletLedger> RecentWalletEntries { get; init; } = [];
}

public sealed class StudentDashboardViewModel
{
    public User Student { get; init; } = null!;
    public string? AssignedTeacherName { get; init; }
    public string? PrimaryInstrumentName { get; init; }
    public Enrollment? ActiveEnrollment { get; init; }
    public StudentQuotaSummaryDto Quota { get; init; } = null!;
    public IReadOnlyList<CalendarEventDto> UpcomingLessons { get; init; } = [];
    public IReadOnlyList<PracticeNote> RecentPracticeNotes { get; init; } = [];
    public IReadOnlyList<PracticeAssignment> PendingAssignments { get; init; } = [];
    public IReadOnlyList<InstrumentRental> ActiveRentals { get; init; } = [];
    public IReadOnlyList<Payment> RecentPayments { get; init; } = [];
    public IReadOnlyList<PortfolioMedia> MediaItems { get; init; } = [];
}

public sealed class ReferrerDashboardViewModel
{
    public string ReferralCode { get; init; } = string.Empty;
    public string ReferralLink { get; init; } = string.Empty;
    public int TotalInvitedUsers { get; init; }
    public int RegisteredUsersCount { get; init; }
    public int PaidFirstCourseCount { get; init; }
    public int EligibleRewardsCount { get; init; }

    public decimal AvailableBalanceIrr { get; init; }
    public decimal TotalEarnedIrr { get; init; }
    public decimal TotalWithdrawnIrr { get; init; }

    public IReadOnlyList<ReferralHistoryRowViewModel> Referrals { get; init; } = [];
    public IReadOnlyList<WalletLedger> RecentLedgerEntries { get; init; } = [];
}

public sealed record ReferralHistoryRowViewModel(
    string MaskedStudentName,
    string RegisteredJalaliDate,
    bool HasCompletedFirstPayment,
    string RewardStatusFa,
    string RewardSummaryFa);
