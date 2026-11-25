using Hamnava.Core.DateTime;
using Hamnava.Core.Finance;
using Hamnava.Core.Messaging;
using Hamnava.Core.Scheduling;
using Hamnava.Core.Security;
using Hamnava.Core.Services;
using Hamnava.DataLayer.Context;
using Hamnava.Domain.Constants;
using Hamnava.Domain.Entities.Cms;
using Hamnava.Domain.Entities.Education;
using Hamnava.Domain.Entities.Finance;
using Hamnava.Domain.Entities.Identity;
using Hamnava.Domain.Entities.Instruments;
using Hamnava.Domain.Enums;
using Hamnava.Domain.ValueObjects;
using Hamnava.Web.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Hamnava.Web.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize(Policy = "AdminOnly")]
public sealed class ManagementController : Controller
{
    private readonly HamnavaDbContext _db;
    private readonly IFinancialService _financialService;
    private readonly ISchedulingService _schedulingService;
    private readonly IQuotaService _quotaService;
    private readonly ISystemSettingsService _settingsService;
    private readonly IAuthorizationScopeService _authScope;
    private readonly ISmsService _smsService;
    private readonly IJalaliDateService _jalali;

    public ManagementController(
        HamnavaDbContext db,
        IFinancialService financialService,
        ISchedulingService schedulingService,
        IQuotaService quotaService,
        ISystemSettingsService settingsService,
        IAuthorizationScopeService authScope,
        ISmsService smsService,
        IJalaliDateService jalali)
    {
        _db = db;
        _financialService = financialService;
        _schedulingService = schedulingService;
        _quotaService = quotaService;
        _settingsService = settingsService;
        _authScope = authScope;
        _smsService = smsService;
        _jalali = jalali;
    }

    [HttpGet]
    public async Task<IActionResult> Index(string tab = "users", CancellationToken ct = default)
    {
        ViewData["Title"] = "مدیریت جامع آکادمی | هم‌نوا";
        ViewData["ActiveTab"] = tab;

        ViewBag.Instruments = await _db.Instruments.AsNoTracking().OrderBy(i => i.DisplayOrder).ToListAsync(ct);
        ViewBag.InstrumentCategories = await _db.InstrumentCategories.AsNoTracking().OrderBy(c => c.DisplayOrder).ToListAsync(ct);
        ViewBag.Rooms = await _db.Rooms.AsNoTracking().Include(r => r.SupportedInstruments).ThenInclude(si => si.Instrument).OrderBy(r => r.Name).ToListAsync(ct);
        ViewBag.CoursePlans = await _db.CoursePlans.AsNoTracking().OrderBy(c => c.DisplayOrder).ToListAsync(ct);
        ViewBag.ConsultationPlans = await _db.ConsultationPlans.AsNoTracking().OrderBy(c => c.DisplayOrder).ToListAsync(ct);
        ViewBag.Discounts = await _db.DiscountCodes.AsNoTracking().OrderByDescending(d => d.CreatedAtUtc).Take(30).ToListAsync(ct);
        ViewBag.Users = await _db.Users.AsNoTracking().Include(u => u.UserRoles).ThenInclude(ur => ur.Role).OrderByDescending(u => u.CreatedAtUtc).Take(50).ToListAsync(ct);
        ViewBag.FinancialSettings = await _db.FinancialSettings.AsNoTracking().FirstOrDefaultAsync(ct);
        ViewBag.SystemSettings = await _db.SystemSettings.AsNoTracking().OrderBy(s => s.Category).ThenBy(s => s.Key).ToListAsync(ct);
        ViewBag.SmsTemplates = await _db.SmsTemplates.AsNoTracking().OrderBy(t => t.TriggerType).ToListAsync(ct);
        ViewBag.PendingWithdrawals = await _db.WalletWithdrawals.AsNoTracking().Include(w => w.Wallet).ThenInclude(wal => wal.User).OrderByDescending(w => w.CreatedAtUtc).Take(20).ToListAsync(ct);

        return View();
    }

    /// <summary>
    /// Searchable database-driven Referrer picker used during student creation/editing.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> SearchReferrers(string? q, CancellationToken ct)
    {
        var referrerRoleId = await _db.Roles.AsNoTracking().Where(r => r.Name == RoleNames.Referrer).Select(r => r.Id).FirstOrDefaultAsync(ct);
        var query = _db.Users.AsNoTracking()
            .Where(u => u.Status == UserStatus.Active && u.UserRoles.Any(ur => ur.RoleId == referrerRoleId));

        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim();
            query = query.Where(u =>
                u.FirstName.Contains(term) ||
                u.LastName.Contains(term) ||
                u.MobileNumber.Contains(term) ||
                (u.ReferralCode != null && u.ReferralCode.Contains(term)));
        }

        var items = await query
            .OrderBy(u => u.LastName)
            .Take(20)
            .Select(u => new
            {
                id = u.Id,
                fullName = u.FirstName + " " + u.LastName,
                mobile = u.MobileNumber,
                referralCode = u.ReferralCode
            })
            .ToListAsync(ct);

        return Json(items);
    }

    [HttpPost]
    public async Task<IActionResult> CreateInstrument(
        string name,
        string slug,
        Guid? categoryId,
        bool isRentalEnabled,
        decimal dailyRentalPriceIrr,
        decimal monthlyRentalPriceIrr,
        int totalInventoryQuantity,
        CancellationToken ct)
    {
        var actor = HttpContext.ToActorContext();
        var resolvedCategoryId = categoryId
            ?? await _db.InstrumentCategories.AsNoTracking().OrderBy(c => c.DisplayOrder).Select(c => (Guid?)c.Id).FirstOrDefaultAsync(ct);

        if (resolvedCategoryId is null)
        {
            var defaultCategory = new InstrumentCategory
            {
                Name = "سازهای عمومی",
                Slug = "general-instruments",
                IsActive = true,
                DisplayOrder = 1
            };
            _db.InstrumentCategories.Add(defaultCategory);
            await _db.SaveChangesAsync(ct);
            resolvedCategoryId = defaultCategory.Id;
        }

        var entity = new Instrument
        {
            CategoryId = resolvedCategoryId.Value,
            Name = name.Trim(),
            Slug = slug.Trim().ToLowerInvariant(),
            IsActive = true,
            IsRentable = isRentalEnabled,
            DailyRentalPriceIrr = dailyRentalPriceIrr,
            MonthlyRentalPriceIrr = monthlyRentalPriceIrr,
            TotalInventoryQuantity = Math.Max(0, totalInventoryQuantity),
            AvailableInventoryQuantity = Math.Max(0, totalInventoryQuantity)
        };

        _db.Instruments.Add(entity);
        await _db.SaveChangesAsync(ct);
        await _authScope.WriteAuditLogAsync(actor, "CreateInstrument", nameof(Instrument), entity.Id.ToString(), afterState: entity, ct: ct);

        TempData["SuccessMessage"] = $"ساز «{entity.Name}» با موفقیت اضافه شد.";
        return RedirectToAction(nameof(Index), new { tab = "instruments" });
    }

    [HttpPost]
    public async Task<IActionResult> CreateClassroom(
        string name,
        string? description,
        int maxConcurrentSessions,
        Guid[] supportedInstrumentIds,
        CancellationToken ct)
    {
        var actor = HttpContext.ToActorContext();
        var room = new Room
        {
            Name = name.Trim(),
            Description = description?.Trim(),
            Capacity = Math.Max(1, maxConcurrentSessions),
            IsActive = true
        };

        foreach (var instId in (supportedInstrumentIds ?? []).Distinct())
        {
            room.SupportedInstruments.Add(new RoomSupportedInstrument { InstrumentId = instId });
        }

        _db.Rooms.Add(room);
        await _db.SaveChangesAsync(ct);
        await _authScope.WriteAuditLogAsync(actor, "CreateRoom", nameof(Room), room.Id.ToString(), afterState: new { room.Name, room.Capacity }, ct: ct);

        TempData["SuccessMessage"] = $"اتاق «{room.Name}» با ظرفیت {room.Capacity} جلسه هم‌زمان ایجاد شد.";
        return RedirectToAction(nameof(Index), new { tab = "rooms" });
    }

    [HttpPost]
    public async Task<IActionResult> CreateCoursePlan(
        string title,
        int sessionCount,
        int lessonDurationMinutes,
        decimal basePriceIrr,
        int coursePeriodMonths,
        CancellationToken ct)
    {
        var actor = HttpContext.ToActorContext();
        var plan = new CoursePlan
        {
            Title = title.Trim(),
            SessionCount = Math.Max(1, sessionCount),
            LessonDurationMinutes = Math.Max(15, lessonDurationMinutes),
            BasePriceIrr = Math.Max(0, basePriceIrr),
            CoursePeriodMonths = Math.Max(1, coursePeriodMonths),
            EffectiveFromGregorian = _jalali.TehranToday,
            IsActive = true
        };

        _db.CoursePlans.Add(plan);
        await _db.SaveChangesAsync(ct);
        await _authScope.WriteAuditLogAsync(actor, "CreateCoursePlan", nameof(CoursePlan), plan.Id.ToString(), afterState: plan, ct: ct);

        TempData["SuccessMessage"] = $"پکیج آموزشی «{plan.Title}» ثبت شد.";
        return RedirectToAction(nameof(Index), new { tab = "plans" });
    }

    [HttpPost]
    public async Task<IActionResult> AssignTeacherToStudent(Guid studentUserId, Guid teacherUserId, Guid instrumentId, CancellationToken ct)
    {
        var actor = HttpContext.ToActorContext();
        var existingActive = await _db.TeacherStudentAssignments
            .Where(a => a.StudentUserId == studentUserId && a.InstrumentId == instrumentId && a.IsActive)
            .ToListAsync(ct);

        foreach (var old in existingActive)
        {
            old.IsActive = false;
            old.EndedAtUtc = _jalali.UtcNow;
        }

        _db.TeacherStudentAssignments.Add(new TeacherStudentAssignment
        {
            StudentUserId = studentUserId,
            TeacherUserId = teacherUserId,
            InstrumentId = instrumentId,
            IsActive = true,
            AssignedAtUtc = _jalali.UtcNow,
            AssignedByUserId = actor.UserId
        });

        await _db.SaveChangesAsync(ct);
        await _authScope.WriteAuditLogAsync(actor, "AssignTeacherToStudent", nameof(TeacherStudentAssignment), studentUserId.ToString(), afterState: new { studentUserId, teacherUserId, instrumentId }, ct: ct);

        TempData["SuccessMessage"] = "تخصیص استاد به هنرجو با موفقیت انجام شد.";
        return RedirectToAction(nameof(Index), new { tab = "users" });
    }

    [HttpPost]
    public async Task<IActionResult> UpdateFinancialSettings(
        decimal defaultTeacherCommissionPercentage,
        CommissionCalculationBasis commissionBasis,
        ReferrerCommissionType defaultReferrerCommissionType,
        decimal defaultReferrerCommissionValue,
        bool deductReferrerShareFromAcademyShare,
        decimal minimumWithdrawalAmountIrr,
        CancellationToken ct)
    {
        var actor = HttpContext.ToActorContext();
        var settings = await _db.FinancialSettings.FirstOrDefaultAsync(ct);
        if (settings is null)
        {
            settings = new FinancialSetting();
            _db.FinancialSettings.Add(settings);
        }

        settings.DefaultTeacherCommissionPercentage = Math.Clamp(defaultTeacherCommissionPercentage, 0m, 100m);
        settings.DefaultAcademyCommissionPercentage = Math.Max(0m, 100m - settings.DefaultTeacherCommissionPercentage);
        settings.CalculationBasis = commissionBasis;
        settings.DefaultReferrerCommissionType = defaultReferrerCommissionType;
        settings.DefaultReferrerCommissionValue = Math.Max(0m, defaultReferrerCommissionValue);
        settings.DeductReferrerShareFromAcademyShare = deductReferrerShareFromAcademyShare;
        settings.MinimumWithdrawalAmountIrr = Math.Max(0m, minimumWithdrawalAmountIrr);

        await _db.SaveChangesAsync(ct);
        await _authScope.WriteAuditLogAsync(actor, "UpdateFinancialSettings", nameof(FinancialSetting), settings.Id.ToString(), afterState: settings, ct: ct);

        TempData["SuccessMessage"] = "تنظیمات مالی با موفقیت ذخیره شد.";
        return RedirectToAction(nameof(Index), new { tab = "finance" });
    }

    [HttpPost]
    public async Task<IActionResult> UpdateSystemSetting(string key, string value, CancellationToken ct)
    {
        var actor = HttpContext.ToActorContext();
        await _settingsService.UpdateSettingAsync(key, value, actor.UserId, ct);
        await _authScope.WriteAuditLogAsync(actor, "UpdateSystemSetting", nameof(SystemSetting), key, afterState: new { key, value }, ct: ct);

        TempData["SuccessMessage"] = $"تنظیم «{key}» به‌روزرسانی شد.";
        return RedirectToAction(nameof(Index), new { tab = "settings" });
    }

    [HttpPost]
    public async Task<IActionResult> ConfirmWithdrawal(Guid withdrawalId, string bankReferenceNumber, string? notes, CancellationToken ct)
    {
        var actor = HttpContext.ToActorContext();
        var result = await _financialService.ConfirmWithdrawalPaymentAsync(withdrawalId, bankReferenceNumber, notes, actor, ct);
        TempData[result.Succeeded ? "SuccessMessage" : "ErrorMessage"] = string.IsNullOrWhiteSpace(result.Message) ? "تسویه برداشت تایید شد." : result.Message;
        return RedirectToAction(nameof(Index), new { tab = "finance" });
    }

    [HttpPost]
    public async Task<IActionResult> ResetStudentQuota(Guid studentUserId, string reason, CancellationToken ct)
    {
        var actor = HttpContext.ToActorContext();
        var result = await _quotaService.ResetStudentQuotaAsync(studentUserId, reason, actor, ct);
        TempData[result.Succeeded ? "SuccessMessage" : "ErrorMessage"] = string.IsNullOrWhiteSpace(result.Message) ? "سهمیه هنرجو بازنشانی شد." : result.Message;
        return RedirectToAction(nameof(Index), new { tab = "users" });
    }

    [HttpPost]
    public async Task<IActionResult> UpdateSmsTemplate(Guid templateId, string bodyTemplate, bool isActive, CancellationToken ct)
    {
        var template = await _db.SmsTemplates.FirstOrDefaultAsync(t => t.Id == templateId, ct);
        if (template is null)
        {
            return NotFound();
        }

        template.BodyTemplate = bodyTemplate.Trim();
        template.IsActive = isActive;
        await _db.SaveChangesAsync(ct);

        TempData["SuccessMessage"] = "قالب پیامک با موفقیت ویرایش شد.";
        return RedirectToAction(nameof(Index), new { tab = "sms" });
    }
}
