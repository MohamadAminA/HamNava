using System.Security.Claims;
using Hamnava.Core.DateTime;
using Hamnava.Core.Payments;
using Hamnava.Core.Services;
using Hamnava.DataLayer.Context;
using Hamnava.Domain.Constants;
using Hamnava.Domain.Enums;
using Hamnava.Web.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Hamnava.Web.Controllers;

[Route("consultation")]
public sealed class ConsultationController : Controller
{
    private readonly HamnavaDbContext _db;
    private readonly IConsultationService _consultationService;
    private readonly IPaymentOrchestrationService _paymentService;
    private readonly IJalaliDateService _jalali;

    public ConsultationController(
        HamnavaDbContext db,
        IConsultationService consultationService,
        IPaymentOrchestrationService paymentService,
        IJalaliDateService jalali)
    {
        _db = db;
        _consultationService = consultationService;
        _paymentService = paymentService;
        _jalali = jalali;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var vm = await BuildPageViewModelAsync(new ConsultationBookingPageViewModel
        {
            JalaliDate = _jalali.FormatDate(_jalali.TehranToday.AddDays(1), usePersianDigits: false)
        }, ct);

        ViewData["Title"] = "رزرو جلسه مشاوره و تعیین سطح | آکادمی موسیقی هم‌نوا";
        return View(vm);
    }

    [HttpPost("")]
    public async Task<IActionResult> Book(ConsultationBookingPageViewModel model, CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            var invalidVm = await BuildPageViewModelAsync(model, ct);
            return View("Index", invalidVm);
        }

        if (!_jalali.TryParseJalaliDate(model.JalaliDate, out var gregorianDate))
        {
            ModelState.AddModelError(nameof(model.JalaliDate), "فرمت تاریخ شمسی معتبر نیست (مثال: 1404/02/15).");
            return View("Index", await BuildPageViewModelAsync(model, ct));
        }

        if (!TimeOnly.TryParse(model.TimeTehran, out var tehranTime))
        {
            ModelState.AddModelError(nameof(model.TimeTehran), "ساعت انتخاب‌شده معتبر نیست.");
            return View("Index", await BuildPageViewModelAsync(model, ct));
        }

        var startUtc = _jalali.ToUtcFromTehran(gregorianDate.ToDateTime(tehranTime));
        Guid? authUserId = Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var parsedUid)
            ? parsedUid
            : null;

        var bookingResult = await _consultationService.BookConsultationAsync(
            new BookConsultationInput(
                model.SelectedPlanId,
                model.SelectedConsultantUserId,
                startUtc,
                model.VisitorFullName,
                model.VisitorMobile,
                model.Notes,
                authUserId),
            ct);

        if (!bookingResult.Succeeded || bookingResult.Data is null)
        {
            ModelState.AddModelError(string.Empty, string.IsNullOrWhiteSpace(bookingResult.Message) ? "رزرو مشاوره با خطا مواجه شد." : bookingResult.Message);
            return View("Index", await BuildPageViewModelAsync(model, ct));
        }

        var booking = bookingResult.Data;

        if (booking.PriceSnapshotIrr > 0m)
        {
            var payerId = authUserId ?? booking.ConsultantUserId;
            var paymentResult = await _paymentService.InitiatePaymentAsync(
                new InitiatePaymentInput(
                    payerId,
                    PaymentType.Consultation,
                    booking.Id,
                    booking.PriceSnapshotIrr,
                    $"رزرو جلسه مشاوره هم‌نوا - {booking.VisitorFullName}",
                    MobileNumber: booking.VisitorMobileNumber),
                ct);

            if (!paymentResult.Succeeded || paymentResult.Data is null)
            {
                ModelState.AddModelError(string.Empty, string.IsNullOrWhiteSpace(paymentResult.Message) ? "اتصال به درگاه پرداخت ناموفق بود." : paymentResult.Message);
                return View("Index", await BuildPageViewModelAsync(model, ct));
            }

            if (paymentResult.Data.RequiresGatewayRedirect && !string.IsNullOrWhiteSpace(paymentResult.Data.RedirectUrl))
            {
                return Redirect(paymentResult.Data.RedirectUrl);
            }
        }

        TempData["SuccessMessage"] = "جلسه مشاوره رایگان شما با موفقیت ثبت و تایید شد. پیامک یادآوری ارسال خواهد شد.";
        return RedirectToAction(nameof(Index));
    }

    private async Task<ConsultationBookingPageViewModel> BuildPageViewModelAsync(
        ConsultationBookingPageViewModel source,
        CancellationToken ct)
    {
        var plans = await _db.ConsultationPlans
            .AsNoTracking()
            .Where(p => p.IsActive)
            .OrderBy(p => p.DisplayOrder)
            .ToListAsync(ct);

        var teacherRoleId = await _db.Roles.AsNoTracking()
            .Where(r => r.Name == RoleNames.Teacher)
            .Select(r => r.Id)
            .FirstOrDefaultAsync(ct);

        var instById = await _db.Instruments.AsNoTracking().ToDictionaryAsync(i => i.Id, i => i.Name, ct);

        var consultantUsers = await _db.Users
            .AsNoTracking()
            .Include(u => u.TeacherInstruments).ThenInclude(ti => ti.Instrument)
            .Where(u => u.Status == UserStatus.Active && u.UserRoles.Any(ur => ur.RoleId == teacherRoleId))
            .ToListAsync(ct);

        var consultants = consultantUsers.Select(u =>
        {
            var instName = u.PrimaryInstrumentId.HasValue && instById.TryGetValue(u.PrimaryInstrumentId.Value, out var nm)
                ? nm
                : u.TeacherInstruments.FirstOrDefault()?.Instrument.Name;

            return new PublicTeacherCardViewModel(
                u.Id,
                u.FullName,
                u.PublicSlug ?? u.Id.ToString("N"),
                !string.IsNullOrWhiteSpace(instName) ? $"مشاور و مدرس {instName}" : "مشاور آموزشی",
                u.Bio,
                string.IsNullOrWhiteSpace(u.ProfileImagePath) ? "/img/logo.webp" : u.ProfileImagePath);
        }).ToList();

        return new ConsultationBookingPageViewModel
        {
            Plans = plans,
            Consultants = consultants,
            SelectedPlanId = source.SelectedPlanId != Guid.Empty ? source.SelectedPlanId : plans.FirstOrDefault()?.Id ?? Guid.Empty,
            SelectedConsultantUserId = source.SelectedConsultantUserId != Guid.Empty ? source.SelectedConsultantUserId : consultants.FirstOrDefault()?.UserId ?? Guid.Empty,
            JalaliDate = source.JalaliDate,
            TimeTehran = source.TimeTehran,
            VisitorFullName = source.VisitorFullName,
            VisitorMobile = source.VisitorMobile,
            Notes = source.Notes
        };
    }
}
