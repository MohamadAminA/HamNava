using Hamnava.Core.DateTime;
using Hamnava.Core.Finance;
using Hamnava.Core.Scheduling;
using Hamnava.Core.Security;
using Hamnava.DataLayer.Context;
using Hamnava.Domain.Enums;
using Hamnava.Web.Infrastructure;
using Hamnava.Web.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Hamnava.Web.Areas.Teacher.Controllers;

[Area("Teacher")]
[Authorize(Policy = "TeacherOrAdmin")]
public sealed class DashboardController : Controller
{
    private readonly HamnavaDbContext _db;
    private readonly ISchedulingService _scheduling;
    private readonly IAuthorizationScopeService _authScope;
    private readonly IFinancialService _financialService;
    private readonly IJalaliDateService _jalali;

    public DashboardController(
        HamnavaDbContext db,
        ISchedulingService scheduling,
        IAuthorizationScopeService authScope,
        IFinancialService financialService,
        IJalaliDateService jalali)
    {
        _db = db;
        _scheduling = scheduling;
        _authScope = authScope;
        _financialService = financialService;
        _jalali = jalali;
    }

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var actor = HttpContext.ToActorContext();
        var teacher = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == actor.UserId, ct);
        if (teacher is null)
        {
            return Unauthorized();
        }

        var assignedIds = await _authScope.GetAccessibleStudentIdsForTeacherAsync(actor.UserId, ct);
        var todayTehran = _jalali.TehranToday;
        var todayStartUtc = _jalali.ToUtcFromTehran(todayTehran.ToDateTime(TimeOnly.MinValue));
        var todayEndUtc = _jalali.ToUtcFromTehran(todayTehran.AddDays(1).ToDateTime(TimeOnly.MinValue));

        var todayClasses = await _scheduling.GetCalendarEventsAsync(actor, todayStartUtc, todayEndUtc, teacherIdFilter: actor.UserId, ct: ct);
        var nextWeekClasses = await _scheduling.GetCalendarEventsAsync(actor, _jalali.UtcNow, _jalali.UtcNow.AddDays(7), teacherIdFilter: actor.UserId, ct: ct);

        var wallet = await _db.Wallets.AsNoTracking().FirstOrDefaultAsync(w => w.UserId == actor.UserId, ct);
        var recentEntries = wallet is null
            ? []
            : await _db.WalletLedgers.AsNoTracking()
                .Where(l => l.WalletId == wallet.Id)
                .OrderByDescending(l => l.CreatedAtUtc)
                .Take(10)
                .ToListAsync(ct);

        var vm = new TeacherDashboardViewModel
        {
            TeacherFullName = teacher.FullName,
            AssignedStudentsCount = assignedIds.Count,
            ActiveEnrollmentsCount = await _db.Enrollments.AsNoTracking().CountAsync(e => e.TeacherUserId == actor.UserId && e.Status == EnrollmentStatus.Active, ct),
            PendingAttendanceCount = await _db.ClassSessions.AsNoTracking().CountAsync(s => s.TeacherUserId == actor.UserId && s.Status == ClassSessionStatus.Scheduled && s.EndAtUtc < _jalali.UtcNow, ct),
            UpcomingMakeupsCount = await _db.ClassSessions.AsNoTracking().CountAsync(s => s.TeacherUserId == actor.UserId && s.SessionType == ClassSessionType.Makeup && s.StartAtUtc >= _jalali.UtcNow, ct),
            UpcomingReschedulesCount = await _db.RescheduleRequests.AsNoTracking().CountAsync(r => r.ClassSession.TeacherUserId == actor.UserId && r.Status == RequestWorkflowStatus.Pending, ct),
            PendingRentalRequestsCount = await _db.InstrumentRentals.AsNoTracking().CountAsync(r => assignedIds.Contains(r.StudentUserId) && r.Status == RentalStatus.PendingPayment, ct),
            AvailableWalletBalanceIrr = wallet?.AvailableBalanceIrr ?? 0m,
            TotalEarnedIrr = wallet?.TotalEarnedIrr ?? 0m,
            TotalWithdrawnIrr = wallet?.TotalWithdrawnIrr ?? 0m,
            NextClass = nextWeekClasses.FirstOrDefault(),
            TodayClasses = todayClasses,
            RecentWalletEntries = recentEntries
        };

        ViewData["Title"] = "داشبورد استاد | آکادمی موسیقی هم‌نوا";
        return View(vm);
    }

    [HttpPost]
    public async Task<IActionResult> RequestWithdrawal(decimal amountIrr, string destinationSheba, CancellationToken ct)
    {
        var actor = HttpContext.ToActorContext();
        var result = await _financialService.RequestWithdrawalAsync(amountIrr, destinationSheba, actor, ct);
        TempData[result.Succeeded ? "SuccessMessage" : "ErrorMessage"] = string.IsNullOrWhiteSpace(result.Message) ? "درخواست برداشت با موفقیت ثبت شد." : result.Message;
        return RedirectToAction(nameof(Index));
    }
}
