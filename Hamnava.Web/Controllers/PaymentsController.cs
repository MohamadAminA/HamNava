using Hamnava.Core.Payments;
using Hamnava.DataLayer.Context;
using Hamnava.Domain.Enums;
using Hamnava.Web.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Hamnava.Web.Controllers;

[Route("payments")]
public sealed class PaymentsController : Controller
{
    private readonly HamnavaDbContext _db;
    private readonly IPaymentOrchestrationService _paymentOrchestrator;

    public PaymentsController(HamnavaDbContext db, IPaymentOrchestrationService paymentOrchestrator)
    {
        _db = db;
        _paymentOrchestrator = paymentOrchestrator;
    }

    [Authorize]
    [HttpPost("start-enrollment/{enrollmentId:guid}")]
    public async Task<IActionResult> StartEnrollmentPayment(
        Guid enrollmentId,
        [FromForm] string? discountCode,
        CancellationToken ct)
    {
        var actor = HttpContext.ToActorContext();
        var enrollment = await _db.Enrollments
            .Include(e => e.CoursePlan)
            .Include(e => e.Student)
            .FirstOrDefaultAsync(e => e.Id == enrollmentId, ct);

        if (enrollment is null)
        {
            return NotFound();
        }

        if (!actor.IsAdmin && enrollment.StudentUserId != actor.UserId)
        {
            return Forbid();
        }

        var result = await _paymentOrchestrator.InitiatePaymentAsync(
            new InitiatePaymentInput(
                enrollment.StudentUserId,
                PaymentType.Tuition,
                enrollment.Id,
                enrollment.OriginalPriceIrr,
                $"شهریه دوره {enrollment.CoursePlan.Title} - آکادمی موسیقی هم‌نوا",
                discountCode,
                enrollment.CoursePlanId,
                enrollment.TeacherUserId,
                enrollment.Student.MobileNumber),
            ct);

        if (!result.Succeeded || result.Data is null)
        {
            TempData["ErrorMessage"] = string.IsNullOrWhiteSpace(result.Message) ? "شروع فرآیند پرداخت ناموفق بود." : result.Message;
            return RedirectToAction("Index", "Dashboard", new { area = "Student" });
        }

        if (result.Data.RequiresGatewayRedirect && !string.IsNullOrWhiteSpace(result.Data.RedirectUrl))
        {
            return Redirect(result.Data.RedirectUrl);
        }

        return RedirectToAction(nameof(Result), new { paymentId = result.Data.PaymentId });
    }

    [Authorize]
    [HttpPost("start-rental/{rentalId:guid}")]
    public async Task<IActionResult> StartRentalPayment(Guid rentalId, CancellationToken ct)
    {
        var actor = HttpContext.ToActorContext();
        var rental = await _db.InstrumentRentals
            .Include(r => r.Instrument)
            .Include(r => r.Student)
            .FirstOrDefaultAsync(r => r.Id == rentalId, ct);

        if (rental is null)
        {
            return NotFound();
        }

        if (!actor.IsAdmin && rental.StudentUserId != actor.UserId)
        {
            return Forbid();
        }

        var result = await _paymentOrchestrator.InitiatePaymentAsync(
            new InitiatePaymentInput(
                rental.StudentUserId,
                PaymentType.InstrumentRental,
                rental.Id,
                rental.FinalPayableAmountIrr,
                $"اجاره ساز {rental.Instrument.Name} - آکادمی موسیقی هم‌نوا",
                MobileNumber: rental.Student.MobileNumber),
            ct);

        if (!result.Succeeded || result.Data is null)
        {
            TempData["ErrorMessage"] = string.IsNullOrWhiteSpace(result.Message) ? "شروع پرداخت اجاره ساز ناموفق بود." : result.Message;
            return RedirectToAction("Index", "Dashboard", new { area = "Student" });
        }

        if (result.Data.RequiresGatewayRedirect && !string.IsNullOrWhiteSpace(result.Data.RedirectUrl))
        {
            return Redirect(result.Data.RedirectUrl);
        }

        return RedirectToAction(nameof(Result), new { paymentId = result.Data.PaymentId });
    }

    [HttpGet("zarinpal/callback")]
    public async Task<IActionResult> ZarinpalCallback(
        [FromQuery(Name = "Authority")] string? authority,
        [FromQuery(Name = "Status")] string? status,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(authority))
        {
            ViewData["Title"] = "نتیجه پرداخت | هم‌نوا";
            return View("Result", new PaymentCallbackResult(Guid.Empty, false, false, null, 0m, "شناسه تراکنش درگاه نامعتبر است."));
        }

        var verifyResult = await _paymentOrchestrator.HandleZarinpalCallbackAsync(
            authority,
            status ?? "NOK",
            ct);

        ViewData["Title"] = verifyResult.Succeeded ? "پرداخت موفق | هم‌نوا" : "پرداخت ناموفق | هم‌نوا";
        return View("Result", verifyResult.Data ?? new PaymentCallbackResult(
            Guid.Empty,
            false,
            false,
            null,
            0m,
            string.IsNullOrWhiteSpace(verifyResult.Message) ? "تایید پرداخت ناموفق بود." : verifyResult.Message));
    }

    [HttpGet("result/{paymentId:guid}")]
    public async Task<IActionResult> Result(Guid paymentId, CancellationToken ct)
    {
        var payment = await _db.Payments.AsNoTracking().FirstOrDefaultAsync(p => p.Id == paymentId, ct);
        if (payment is null)
        {
            return NotFound();
        }

        var succeeded = payment.Status == PaymentStatus.Verified;
        ViewData["Title"] = succeeded ? "پرداخت موفق | هم‌نوا" : "وضعیت پرداخت | هم‌نوا";
        return View(new PaymentCallbackResult(
            payment.Id,
            succeeded,
            false,
            payment.GatewayRefId,
            payment.FinalAmountIrr,
            succeeded ? "تراکنش با موفقیت تایید و ثبت شد." : "تراکنش تایید نشده است."));
    }
}
