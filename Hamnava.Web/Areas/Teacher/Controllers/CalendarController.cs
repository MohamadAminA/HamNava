using Hamnava.Core.DateTime;
using Hamnava.Core.Media;
using Hamnava.Core.Messaging;
using Hamnava.Core.Scheduling;
using Hamnava.Core.Security;
using Hamnava.Core.Services;
using Hamnava.DataLayer.Context;
using Hamnava.Domain.Entities.Education;
using Hamnava.Domain.Entities.Finance;
using Hamnava.Domain.Enums;
using Hamnava.Web.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Hamnava.Web.Areas.Teacher.Controllers;

[Area("Teacher")]
[Authorize(Policy = "TeacherOrAdmin")]
public sealed class CalendarController : Controller
{
    private readonly HamnavaDbContext _db;
    private readonly ISchedulingService _scheduling;
    private readonly IAttendanceService _attendance;
    private readonly IAuthorizationScopeService _authScope;
    private readonly IMediaProcessingService _mediaService;
    private readonly ISmsService _smsService;
    private readonly IJalaliDateService _jalali;

    public CalendarController(
        HamnavaDbContext db,
        ISchedulingService scheduling,
        IAttendanceService attendance,
        IAuthorizationScopeService authScope,
        IMediaProcessingService mediaService,
        ISmsService smsService,
        IJalaliDateService jalali)
    {
        _db = db;
        _scheduling = scheduling;
        _attendance = attendance;
        _authScope = authScope;
        _mediaService = mediaService;
        _smsService = smsService;
        _jalali = jalali;
    }

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var actor = HttpContext.ToActorContext();
        var studentIds = actor.IsAdmin
            ? await _db.Users.AsNoTracking().Select(u => u.Id).ToListAsync(ct)
            : await _authScope.GetAccessibleStudentIdsForTeacherAsync(actor.UserId, ct);

        ViewBag.Rooms = await _db.Rooms.AsNoTracking().Where(r => r.IsActive).OrderBy(r => r.Name).ToListAsync(ct);
        ViewBag.Students = await _db.Users.AsNoTracking().Where(u => studentIds.Contains(u.Id)).OrderBy(u => u.LastName).ToListAsync(ct);
        ViewBag.TodayJalali = _jalali.FormatDate(_jalali.TehranToday);
        ViewData["Title"] = "تقویم آموزشی و کلاس‌ها | هم‌نوا";
        return View();
    }

    [HttpGet]
    public async Task<IActionResult> Events(DateTime? startUtc, DateTime? endUtc, Guid? roomId, Guid? studentId, CancellationToken ct)
    {
        var actor = HttpContext.ToActorContext();
        var from = startUtc ?? _jalali.UtcNow.Date.AddDays(-7);
        var to = endUtc ?? _jalali.UtcNow.Date.AddDays(30);

        var events = await _scheduling.GetCalendarEventsAsync(
            actor,
            DateTime.SpecifyKind(from, DateTimeKind.Utc),
            DateTime.SpecifyKind(to, DateTimeKind.Utc),
            teacherIdFilter: actor.IsAdmin ? null : actor.UserId,
            studentIdFilter: studentId,
            roomIdFilter: roomId,
            ct: ct);

        return Json(events);
    }

    public sealed record MoveSessionPayload(Guid SessionId, DateTime NewStartAtUtc, Guid? NewRoomId, string? Reason);

    /// <summary>
    /// AJAX endpoint backing Jalali Calendar Drag & Drop with server-authoritative conflict checks.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> MoveSession([FromBody] MoveSessionPayload payload, CancellationToken ct)
    {
        var actor = HttpContext.ToActorContext();
        var result = await _scheduling.MoveSessionDragDropAsync(
            new DragDropMoveRequest(
                payload.SessionId,
                DateTime.SpecifyKind(payload.NewStartAtUtc, DateTimeKind.Utc),
                payload.NewRoomId,
                payload.Reason),
            actor,
            ct);

        if (!result.Succeeded)
        {
            return BadRequest(new { succeeded = false, code = result.ErrorCode, message = result.Message });
        }

        return Json(new { succeeded = true, message = result.Message, updatedEvent = result.Data });
    }

    public sealed record MarkAttendancePayload(Guid SessionId, ClassSessionStatus Status, string? Notes);

    [HttpPost]
    public async Task<IActionResult> MarkAttendance([FromBody] MarkAttendancePayload payload, CancellationToken ct)
    {
        var actor = HttpContext.ToActorContext();
        var result = await _attendance.MarkAttendanceAsync(payload.SessionId, payload.Status, payload.Notes, actor, ct);
        if (!result.Succeeded)
        {
            return BadRequest(new { succeeded = false, code = result.ErrorCode, message = result.Message });
        }

        return Json(new { succeeded = true, message = result.Message });
    }

    [HttpPost]
    public async Task<IActionResult> AddPracticeNote(
        Guid studentUserId,
        Guid? classSessionId,
        string title,
        string content,
        string? assignmentTitle,
        string? assignmentInstructions,
        CancellationToken ct)
    {
        var actor = HttpContext.ToActorContext();
        if (!await _authScope.CanAccessStudentAsync(actor, studentUserId, ct))
        {
            return Forbid();
        }

        var resolvedSessionId = classSessionId
            ?? await _db.ClassSessions.AsNoTracking()
                .Where(s => s.StudentUserId == studentUserId && (actor.IsAdmin || s.TeacherUserId == actor.UserId))
                .OrderByDescending(s => s.StartAtUtc)
                .Select(s => (Guid?)s.Id)
                .FirstOrDefaultAsync(ct);

        if (resolvedSessionId is null)
        {
            TempData["ErrorMessage"] = "برای ثبت یادداشت تمرینی، ابتدا باید حداقل یک جلسه کلاسی برای هنرجو تعریف شده باشد.";
            return RedirectToAction(nameof(Index));
        }

        _db.PracticeNotes.Add(new PracticeNote
        {
            StudentUserId = studentUserId,
            TeacherUserId = actor.UserId,
            ClassSessionId = resolvedSessionId.Value,
            Title = title.Trim(),
            Content = content.Trim()
        });

        if (!string.IsNullOrWhiteSpace(assignmentTitle))
        {
            _db.PracticeAssignments.Add(new PracticeAssignment
            {
                StudentUserId = studentUserId,
                TeacherUserId = actor.UserId,
                ClassSessionId = resolvedSessionId.Value,
                Title = assignmentTitle.Trim(),
                Instructions = assignmentInstructions?.Trim() ?? string.Empty
            });
        }

        await _db.SaveChangesAsync(ct);
        TempData["SuccessMessage"] = "یادداشت تمرینی و تکلیف هنرجو ثبت شد.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [RequestSizeLimit(120 * 1024 * 1024)]
    public async Task<IActionResult> UploadStudentMedia(
        Guid studentUserId,
        string title,
        string? description,
        bool requestPublicVisibility,
        IFormFile file,
        CancellationToken ct)
    {
        var actor = HttpContext.ToActorContext();
        if (file is null || file.Length == 0)
        {
            TempData["ErrorMessage"] = "لطفاً یک فایل تصویر یا ویدیو انتخاب کنید.";
            return RedirectToAction(nameof(Index));
        }

        await using var stream = file.OpenReadStream();
        var result = await _mediaService.UploadPortfolioMediaAsync(
            studentUserId,
            title,
            description,
            file.FileName,
            file.ContentType,
            file.Length,
            stream,
            requestPublicVisibility,
            actor,
            ct);

        TempData[result.Succeeded ? "SuccessMessage" : "ErrorMessage"] = string.IsNullOrWhiteSpace(result.Message) ? "فایل دریافت و به صف پردازش ارسال شد." : result.Message;
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    public async Task<IActionResult> CreateTeacherDiscount(
        string code,
        DiscountType discountType,
        decimal value,
        int maxUsages,
        int validDays,
        CancellationToken ct)
    {
        var actor = HttpContext.ToActorContext();
        var normalizedCode = code.Trim().ToUpperInvariant();
        var discount = new DiscountCode
        {
            Code = normalizedCode,
            Title = $"کد تخفیف استاد ({normalizedCode})",
            Scope = DiscountScope.TeacherSpecific,
            OwnerTeacherUserId = actor.UserId,
            DiscountType = discountType,
            DiscountValue = Math.Max(1m, value),
            MaxTotalUsages = Math.Max(1, maxUsages),
            StartsAtUtc = _jalali.UtcNow,
            ExpiresAtUtc = _jalali.UtcNow.AddDays(Math.Max(1, validDays)),
            IsActive = true
        };

        _db.DiscountCodes.Add(discount);
        await _db.SaveChangesAsync(ct);
        TempData["SuccessMessage"] = $"کد تخفیف اختصاصی شما ({discount.Code}) ایجاد شد.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    public async Task<IActionResult> SendStudentSms(Guid studentUserId, string messageBody, CancellationToken ct)
    {
        var actor = HttpContext.ToActorContext();
        if (!await _authScope.CanAccessStudentAsync(actor, studentUserId, ct))
        {
            return Forbid();
        }

        var student = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == studentUserId, ct);
        if (student is null)
        {
            return NotFound();
        }

        var result = await _smsService.SendDirectManualSmsAsync(student.MobileNumber, messageBody, student.Id, actor.UserId, ct);
        TempData[result.Succeeded ? "SuccessMessage" : "ErrorMessage"] = string.IsNullOrWhiteSpace(result.Message) ? "پیامک با موفقیت ارسال شد." : result.Message;
        return RedirectToAction(nameof(Index));
    }
}
