using Hamnava.Core.DateTime;
using Hamnava.Core.Scheduling;
using Hamnava.Core.Services;
using Hamnava.DataLayer.Context;
using Hamnava.Domain.Entities.Media;
using Hamnava.Domain.Enums;
using Hamnava.Web.Infrastructure;
using Hamnava.Web.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Hamnava.Web.Areas.Student.Controllers;

[Area("Student")]
[Authorize(Policy = "StudentOrAdmin")]
public sealed class DashboardController : Controller
{
    private readonly HamnavaDbContext _db;
    private readonly ISchedulingService _scheduling;
    private readonly IQuotaService _quotaService;
    private readonly IAttendanceService _attendanceService;
    private readonly IInstrumentRentalService _rentalService;
    private readonly IJalaliDateService _jalali;

    public DashboardController(
        HamnavaDbContext db,
        ISchedulingService scheduling,
        IQuotaService quotaService,
        IAttendanceService attendanceService,
        IInstrumentRentalService rentalService,
        IJalaliDateService jalali)
    {
        _db = db;
        _scheduling = scheduling;
        _quotaService = quotaService;
        _attendanceService = attendanceService;
        _rentalService = rentalService;
        _jalali = jalali;
    }

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var actor = HttpContext.ToActorContext();
        var student = await _db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == actor.UserId, ct);

        if (student is null)
        {
            return Unauthorized();
        }

        string? primaryInstrumentName = null;
        if (student.PrimaryInstrumentId.HasValue)
        {
            primaryInstrumentName = await _db.Instruments
                .AsNoTracking()
                .Where(i => i.Id == student.PrimaryInstrumentId.Value)
                .Select(i => i.Name)
                .FirstOrDefaultAsync(ct);
        }

        var assignment = await _db.TeacherStudentAssignments
            .AsNoTracking()
            .Include(a => a.Teacher)
            .FirstOrDefaultAsync(a => a.StudentUserId == actor.UserId && a.IsActive, ct);

        var activeEnrollment = await _db.Enrollments
            .AsNoTracking()
            .Include(e => e.CoursePlan)
            .Include(e => e.Instrument)
            .Include(e => e.Teacher)
            .Where(e => e.StudentUserId == actor.UserId && e.Status == EnrollmentStatus.Active)
            .OrderByDescending(e => e.CreatedAtUtc)
            .FirstOrDefaultAsync(ct);

        var quota = await _quotaService.GetCurrentQuotaSummaryAsync(actor.UserId, ct);
        var upcomingLessons = await _scheduling.GetCalendarEventsAsync(
            actor,
            _jalali.UtcNow.AddDays(-1),
            _jalali.UtcNow.AddDays(45),
            studentIdFilter: actor.UserId,
            ct: ct);

        var mediaConsent = await _db.MediaConsents
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.StudentUserId == actor.UserId, ct);

        var vm = new StudentDashboardViewModel
        {
            Student = student,
            AssignedTeacherName = assignment?.Teacher.FullName ?? activeEnrollment?.Teacher.FullName,
            PrimaryInstrumentName = primaryInstrumentName ?? activeEnrollment?.Instrument.Name,
            ActiveEnrollment = activeEnrollment,
            Quota = quota,
            UpcomingLessons = upcomingLessons,
            RecentPracticeNotes = await _db.PracticeNotes.AsNoTracking()
                .Include(n => n.Teacher)
                .Where(n => n.StudentUserId == actor.UserId)
                .OrderByDescending(n => n.CreatedAtUtc)
                .Take(10)
                .ToListAsync(ct),
            PendingAssignments = await _db.PracticeAssignments.AsNoTracking()
                .Where(a => a.StudentUserId == actor.UserId)
                .OrderBy(a => a.IsCompletedByStudent)
                .ThenByDescending(a => a.CreatedAtUtc)
                .Take(10)
                .ToListAsync(ct),
            ActiveRentals = await _db.InstrumentRentals.AsNoTracking()
                .Include(r => r.Instrument)
                .Where(r => r.StudentUserId == actor.UserId)
                .OrderByDescending(r => r.CreatedAtUtc)
                .Take(6)
                .ToListAsync(ct),
            RecentPayments = await _db.Payments.AsNoTracking()
                .Where(p => p.UserId == actor.UserId)
                .OrderByDescending(p => p.CreatedAtUtc)
                .Take(10)
                .ToListAsync(ct),
            MediaItems = await _db.PortfolioMedias.AsNoTracking()
                .Where(m => m.StudentUserId == actor.UserId)
                .OrderByDescending(m => m.CreatedAtUtc)
                .Take(12)
                .ToListAsync(ct)
        };

        ViewBag.HasPublicPortfolioConsent = mediaConsent?.AllowPublicPortfolio ?? false;
        ViewBag.RentableInstruments = await _db.Instruments.AsNoTracking()
            .Where(i => i.IsActive && i.IsRentable && i.AvailableInventoryQuantity > 0)
            .OrderBy(i => i.DisplayOrder)
            .ToListAsync(ct);

        ViewData["Title"] = "پنل هنرجو | آکادمی موسیقی هم‌نوا";
        return View(vm);
    }

    [HttpPost]
    public async Task<IActionResult> RequestAuthorizedAbsence(Guid classSessionId, string? reason, CancellationToken ct)
    {
        var actor = HttpContext.ToActorContext();
        var result = await _attendanceService.RequestStudentAuthorizedAbsenceAsync(classSessionId, reason, actor, ct);
        TempData[result.Succeeded ? "SuccessMessage" : "ErrorMessage"] = string.IsNullOrWhiteSpace(result.Message) ? "غیبت موجه شما ثبت شد." : result.Message;
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    public async Task<IActionResult> RequestRental(
        Guid instrumentId,
        RentalPricingUnit pricingUnit,
        int durationUnits,
        string startJalaliDate,
        string? notes,
        CancellationToken ct)
    {
        var actor = HttpContext.ToActorContext();
        var startDate = _jalali.TryParseJalaliDate(startJalaliDate, out var parsed)
            ? parsed
            : _jalali.TehranToday.AddDays(1);

        var result = await _rentalService.CreateRentalRequestAsync(
            new CreateRentalRequestInput(actor.UserId, instrumentId, pricingUnit, Math.Max(1, durationUnits), startDate, notes),
            actor,
            ct);

        TempData[result.Succeeded ? "SuccessMessage" : "ErrorMessage"] = string.IsNullOrWhiteSpace(result.Message) ? "درخواست اجاره ساز ثبت شد." : result.Message;
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    public async Task<IActionResult> TogglePublicPortfolioConsent(bool isPublicProfileVisible, bool consentGiven, CancellationToken ct)
    {
        var actor = HttpContext.ToActorContext();
        var student = await _db.Users.FirstOrDefaultAsync(u => u.Id == actor.UserId, ct);
        if (student is null)
        {
            return NotFound();
        }

        var consent = await _db.MediaConsents.FirstOrDefaultAsync(c => c.StudentUserId == actor.UserId, ct);
        if (consent is null)
        {
            consent = new MediaConsent
            {
                StudentUserId = actor.UserId
            };
            _db.MediaConsents.Add(consent);
        }

        consent.AllowPublicPortfolio = consentGiven;
        consent.AllowPublicImages = consentGiven;
        consent.AllowPublicVideos = consentGiven;
        consent.ConsentedAtUtc = _jalali.UtcNow;
        consent.ConsentIpAddress = actor.IpAddress;

        student.IsPublicProfileVisible = consentGiven && isPublicProfileVisible;
        await _db.SaveChangesAsync(ct);

        TempData["SuccessMessage"] = "تنظیمات حریم خصوصی پورتفولیو عمومی به‌روزرسانی شد.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    public async Task<IActionResult> CompleteAssignment(Guid assignmentId, CancellationToken ct)
    {
        var actor = HttpContext.ToActorContext();
        var assignment = await _db.PracticeAssignments
            .FirstOrDefaultAsync(a => a.Id == assignmentId && a.StudentUserId == actor.UserId, ct);

        if (assignment is not null)
        {
            assignment.IsCompletedByStudent = true;
            assignment.CompletedAtUtc = _jalali.UtcNow;
            await _db.SaveChangesAsync(ct);
            TempData["SuccessMessage"] = "تکلیف تمرینی به عنوان انجام‌شده ثبت شد.";
        }

        return RedirectToAction(nameof(Index));
    }
}
