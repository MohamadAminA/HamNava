using Hamnava.Core.DateTime;
using Hamnava.Core.Reports;
using Hamnava.Core.Security;
using Hamnava.DataLayer.Context;
using Hamnava.Web.Infrastructure;
using Hamnava.Web.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Hamnava.Web.Controllers;

[Route("certificates")]
public sealed class CertificatesController : Controller
{
    private readonly HamnavaDbContext _db;
    private readonly IPdfCertificateService _certificateService;
    private readonly IAuthorizationScopeService _authScope;
    private readonly IJalaliDateService _jalali;

    public CertificatesController(
        HamnavaDbContext db,
        IPdfCertificateService certificateService,
        IAuthorizationScopeService authScope,
        IJalaliDateService jalali)
    {
        _db = db;
        _certificateService = certificateService;
        _authScope = authScope;
        _jalali = jalali;
    }

    [HttpGet("verify/{token}")]
    public async Task<IActionResult> Verify(string token, CancellationToken ct)
    {
        var cert = await _db.LearningCertificates
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.VerificationToken == token && !c.IsRevoked, ct);

        ViewData["Title"] = "استعلام اصالت گواهینامه | آکادمی موسیقی هم‌نوا";

        if (cert is null)
        {
            return View(new CertificateVerificationViewModel { IsValid = false });
        }

        return View(new CertificateVerificationViewModel
        {
            IsValid = true,
            CertificateNumber = cert.CertificateNumber,
            StudentFullName = cert.StudentFullNameSnapshot,
            InstrumentName = cert.InstrumentNameSnapshot,
            CourseTitle = cert.CourseTitleSnapshot,
            TeacherFullName = cert.TeacherFullNameSnapshot,
            IssuedJalaliDate = _jalali.FormatDate(cert.IssuedAtUtc)
        });
    }

    [Authorize]
    [HttpGet("download/{enrollmentId:guid}")]
    public async Task<IActionResult> Download(Guid enrollmentId, CancellationToken ct)
    {
        var actor = HttpContext.ToActorContext();
        var enrollment = await _db.Enrollments
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == enrollmentId, ct);

        if (enrollment is null)
        {
            return NotFound();
        }

        if (!await _authScope.CanAccessStudentAsync(actor, enrollment.StudentUserId, ct))
        {
            return Forbid();
        }

        var baseUrl = $"{Request.Scheme}://{Request.Host}";
        var result = await _certificateService.IssueOrRenderCertificateAsync(enrollmentId, baseUrl, ct);
        if (!result.Succeeded)
        {
            TempData["ErrorMessage"] = string.IsNullOrWhiteSpace(result.Message) ? "صدور گواهینامه در حال حاضر امکان‌پذیر نیست." : result.Message;
            return RedirectToAction("DashboardRedirect", "Account");
        }

        return File(result.Data.PdfBytes, "application/pdf", $"Hamnava-Certificate-{result.Data.Record.CertificateNumber}.pdf");
    }
}
