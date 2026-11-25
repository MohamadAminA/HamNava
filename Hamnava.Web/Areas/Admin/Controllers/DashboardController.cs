using Hamnava.Core.DateTime;
using Hamnava.Core.Scheduling;
using Hamnava.DataLayer.Context;
using Hamnava.Domain.Constants;
using Hamnava.Domain.Enums;
using Hamnava.Web.Infrastructure;
using Hamnava.Web.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Hamnava.Web.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize(Policy = "AdminOnly")]
public sealed class DashboardController : Controller
{
    private readonly HamnavaDbContext _db;
    private readonly ISchedulingService _scheduling;
    private readonly IJalaliDateService _jalali;

    public DashboardController(
        HamnavaDbContext db,
        ISchedulingService scheduling,
        IJalaliDateService jalali)
    {
        _db = db;
        _scheduling = scheduling;
        _jalali = jalali;
    }

    [HttpGet]
    public async Task<IActionResult> Index(string range = "month", string? from = null, string? to = null, CancellationToken ct = default)
    {
        var (startUtc, endUtc, fromJalali, toJalali) = ResolveDateRange(range, from, to);

        var studentRoleId = await _db.Roles.AsNoTracking().Where(r => r.Name == RoleNames.Student).Select(r => r.Id).FirstOrDefaultAsync(ct);
        var teacherRoleId = await _db.Roles.AsNoTracking().Where(r => r.Name == RoleNames.Teacher).Select(r => r.Id).FirstOrDefaultAsync(ct);
        var referrerRoleId = await _db.Roles.AsNoTracking().Where(r => r.Name == RoleNames.Referrer).Select(r => r.Id).FirstOrDefaultAsync(ct);

        var todayTehran = _jalali.TehranToday;
        var todayStartUtc = _jalali.ToUtcFromTehran(todayTehran.ToDateTime(TimeOnly.MinValue));
        var todayEndUtc = _jalali.ToUtcFromTehran(todayTehran.AddDays(1).ToDateTime(TimeOnly.MinValue));

        var tuitionIrr = await _db.Payments.AsNoTracking()
            .Where(p => p.Status == PaymentStatus.Verified
                        && p.PaymentType == PaymentType.Tuition
                        && p.VerifiedAtUtc >= startUtc && p.VerifiedAtUtc < endUtc)
            .SumAsync(p => (decimal?)p.FinalAmountIrr, ct) ?? 0m;

        var rentalIrr = await _db.Payments.AsNoTracking()
            .Where(p => p.Status == PaymentStatus.Verified
                        && (p.PaymentType == PaymentType.InstrumentRental || p.PaymentType == PaymentType.RentalRenewal)
                        && p.VerifiedAtUtc >= startUtc && p.VerifiedAtUtc < endUtc)
            .SumAsync(p => (decimal?)p.FinalAmountIrr, ct) ?? 0m;

        var teacherLiabilityIrr = await _db.Wallets.AsNoTracking()
            .Where(w => w.User.UserRoles.Any(ur => ur.RoleId == teacherRoleId))
            .SumAsync(w => (decimal?)w.AvailableBalanceIrr, ct) ?? 0m;

        var referrerRewardsIrr = await _db.WalletLedgers.AsNoTracking()
            .Where(l => l.EntryType == WalletLedgerEntryType.ReferralCredit
                        && l.CreatedAtUtc >= startUtc && l.CreatedAtUtc < endUtc)
            .SumAsync(l => (decimal?)l.AmountIrr, ct) ?? 0m;

        var actor = HttpContext.ToActorContext();
        var upcoming = await _scheduling.GetCalendarEventsAsync(
            actor,
            todayStartUtc,
            todayStartUtc.AddDays(7),
            ct: ct);

        var pendingWithdrawals = await _db.WalletWithdrawals
            .AsNoTracking()
            .Include(w => w.Wallet).ThenInclude(wal => wal.User)
            .Where(w => w.Status == WithdrawalStatus.Requested)
            .OrderBy(w => w.CreatedAtUtc)
            .Take(10)
            .ToListAsync(ct);

        var vm = new AdminDashboardViewModel
        {
            ActiveRange = range,
            FromJalali = fromJalali,
            ToJalali = toJalali,
            TotalActiveStudents = await _db.Users.AsNoTracking().CountAsync(u => u.Status == UserStatus.Active && u.UserRoles.Any(r => r.RoleId == studentRoleId), ct),
            TotalActiveTeachers = await _db.Users.AsNoTracking().CountAsync(u => u.Status == UserStatus.Active && u.UserRoles.Any(r => r.RoleId == teacherRoleId), ct),
            TotalActiveReferrers = await _db.Users.AsNoTracking().CountAsync(u => u.Status == UserStatus.Active && u.UserRoles.Any(r => r.RoleId == referrerRoleId), ct),
            ActiveEnrollmentsCount = await _db.Enrollments.AsNoTracking().CountAsync(e => e.Status == EnrollmentStatus.Active, ct),
            NewStudentRegistrations = await _db.Users.AsNoTracking().CountAsync(u => u.CreatedAtUtc >= startUtc && u.CreatedAtUtc < endUtc && u.UserRoles.Any(r => r.RoleId == studentRoleId), ct),
            LessonsScheduledToday = await _db.ClassSessions.AsNoTracking().CountAsync(s => s.StartAtUtc >= todayStartUtc && s.StartAtUtc < todayEndUtc, ct),
            LessonsCompletedToday = await _db.ClassSessions.AsNoTracking().CountAsync(s => s.StartAtUtc >= todayStartUtc && s.StartAtUtc < todayEndUtc && s.Status == ClassSessionStatus.Present, ct),
            AuthorizedAbsencesCount = await _db.ClassSessions.AsNoTracking().CountAsync(s => s.StartAtUtc >= startUtc && s.StartAtUtc < endUtc && s.Status == ClassSessionStatus.AuthorizedAbsence, ct),
            UnauthorizedAbsencesCount = await _db.ClassSessions.AsNoTracking().CountAsync(s => s.StartAtUtc >= startUtc && s.StartAtUtc < endUtc && s.Status == ClassSessionStatus.UnauthorizedAbsence, ct),
            ExpiringRentalsCount = await _db.InstrumentRentals.AsNoTracking().CountAsync(r => (r.Status == RentalStatus.Active || r.Status == RentalStatus.Extended) && r.EndDateGregorian <= todayTehran.AddDays(5), ct),
            PendingRentalsCount = await _db.InstrumentRentals.AsNoTracking().CountAsync(r => r.Status == RentalStatus.PendingPayment, ct),
            PendingPaymentsCount = await _db.Payments.AsNoTracking().CountAsync(p => p.Status == PaymentStatus.Pending, ct),
            TotalReceivedTuitionIrr = tuitionIrr,
            TotalRentalRevenueIrr = rentalIrr,
            TotalTeacherWalletLiabilityIrr = teacherLiabilityIrr,
            TotalReferrerRewardsIrr = referrerRewardsIrr,
            UpcomingLessons = upcoming.Take(12).ToList(),
            PendingWithdrawals = pendingWithdrawals
        };

        ViewData["Title"] = "داشبورد مدیریت | آکادمی موسیقی هم‌نوا";
        return View(vm);
    }

    [HttpGet]
    public async Task<IActionResult> FinancialTrends(CancellationToken ct)
    {
        var today = _jalali.TehranToday;
        var (currentYear, currentMonth, _) = _jalali.ToJalaliParts(today);

        var labels = new List<string>();
        var tuitionSeries = new List<decimal>();
        var rentalSeries = new List<decimal>();

        for (var i = 5; i >= 0; i--)
        {
            var m = currentMonth - i;
            var y = currentYear;
            while (m <= 0)
            {
                m += 12;
                y--;
            }

            var (startDate, endDate) = _jalali.GetJalaliMonthRange(y, m);
            var startUtc = _jalali.ToUtcFromTehran(startDate.ToDateTime(TimeOnly.MinValue));
            var endUtc = _jalali.ToUtcFromTehran(endDate.AddDays(1).ToDateTime(TimeOnly.MinValue));

            var tuition = await _db.Payments.AsNoTracking()
                .Where(p => p.Status == PaymentStatus.Verified && p.PaymentType == PaymentType.Tuition && p.VerifiedAtUtc >= startUtc && p.VerifiedAtUtc < endUtc)
                .SumAsync(p => (decimal?)p.FinalAmountIrr, ct) ?? 0m;

            var rental = await _db.Payments.AsNoTracking()
                .Where(p => p.Status == PaymentStatus.Verified && (p.PaymentType == PaymentType.InstrumentRental || p.PaymentType == PaymentType.RentalRenewal) && p.VerifiedAtUtc >= startUtc && p.VerifiedAtUtc < endUtc)
                .SumAsync(p => (decimal?)p.FinalAmountIrr, ct) ?? 0m;

            labels.Add(_jalali.GetPersianMonthName(m));
            tuitionSeries.Add(Math.Round(tuition / 10m, 0)); // Displayed in Toman on chart
            rentalSeries.Add(Math.Round(rental / 10m, 0));
        }

        return Json(new { labels, tuitionToman = tuitionSeries, rentalToman = rentalSeries });
    }

    private (DateTime StartUtc, DateTime EndUtc, string FromJalali, string ToJalali) ResolveDateRange(string range, string? from, string? to)
    {
        var today = _jalali.TehranToday;
        DateOnly startDate;
        DateOnly endDate = today;

        switch (range.ToLowerInvariant())
        {
            case "today":
                startDate = today;
                break;
            case "week":
                startDate = today.AddDays(-6);
                break;
            case "custom" when !string.IsNullOrWhiteSpace(from) && !string.IsNullOrWhiteSpace(to)
                              && _jalali.TryParseJalaliDate(from, out var parsedFrom)
                              && _jalali.TryParseJalaliDate(to, out var parsedTo):
                startDate = parsedFrom;
                endDate = parsedTo >= parsedFrom ? parsedTo : parsedFrom;
                break;
            default:
                var (jy, jm, _) = _jalali.ToJalaliParts(today);
                (startDate, endDate) = _jalali.GetJalaliMonthRange(jy, jm);
                break;
        }

        var startUtc = _jalali.ToUtcFromTehran(startDate.ToDateTime(TimeOnly.MinValue));
        var endUtc = _jalali.ToUtcFromTehran(endDate.AddDays(1).ToDateTime(TimeOnly.MinValue));
        return (startUtc, endUtc, _jalali.FormatDate(startDate, false), _jalali.FormatDate(endDate, false));
    }
}
