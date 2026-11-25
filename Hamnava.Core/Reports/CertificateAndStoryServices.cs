using System.Security.Cryptography;
using Hamnava.Core.Common;
using Hamnava.Core.DateTime;
using Hamnava.Core.Services;
using Hamnava.DataLayer.Context;
using Hamnava.Domain.Constants;
using Hamnava.Domain.Entities.Education;
using Hamnava.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using QRCoder;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using SkiaSharp;
using SkiaSharp.HarfBuzz;

namespace Hamnava.Core.Reports;

public interface IPdfCertificateService
{
    Task<ServiceResult<(LearningCertificate Record, byte[] PdfBytes)>> IssueOrRenderCertificateAsync(
        Guid enrollmentId,
        string siteBaseUrl,
        CancellationToken ct = default);
}

/// <summary>
/// Server-side Persian RTL learning certificate generator using QuestPDF (Community License) + QRCoder.
/// Validates actual completed sessions before issuing a verifiable certificate.
/// </summary>
public sealed class QuestPdfCertificateService : IPdfCertificateService
{
    private readonly HamnavaDbContext _db;
    private readonly ISystemSettingsService _settings;
    private readonly IJalaliDateService _jalali;

    static QuestPdfCertificateService()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public QuestPdfCertificateService(
        HamnavaDbContext db,
        ISystemSettingsService settings,
        IJalaliDateService jalali)
    {
        _db = db;
        _settings = settings;
        _jalali = jalali;
    }

    public async Task<ServiceResult<(LearningCertificate Record, byte[] PdfBytes)>> IssueOrRenderCertificateAsync(
        Guid enrollmentId,
        string siteBaseUrl,
        CancellationToken ct = default)
    {
        var enrollment = await _db.Enrollments
            .Include(e => e.Student)
            .Include(e => e.Teacher)
            .Include(e => e.Instrument)
            .Include(e => e.Sessions)
            .FirstOrDefaultAsync(e => e.Id == enrollmentId, ct);

        if (enrollment is null)
        {
            return ServiceResult<(LearningCertificate, byte[])>.Fail("ثبت‌نام موردنظر یافت نشد.", "ENROLLMENT_NOT_FOUND");
        }

        var minCompleted = await _settings.GetIntAsync(SystemSettingKeys.CertificateMinCompletedSessions, 4, ct);
        var completedCount = enrollment.Sessions.Count(s => s.Status == ClassSessionStatus.Present);

        if (completedCount < minCompleted)
        {
            return ServiceResult<(LearningCertificate, byte[])>.Fail(
                $"برای دریافت گواهی‌نامه حداقل {minCompleted} جلسه حضور تاییدشده لازم است (جلسات تکمیل‌شده فعلی: {completedCount}).",
                "CERTIFICATE_NOT_ELIGIBLE");
        }

        var cert = await _db.LearningCertificates.FirstOrDefaultAsync(c => c.EnrollmentId == enrollment.Id && !c.IsRevoked, ct);
        if (cert is null)
        {
            cert = new LearningCertificate
            {
                StudentUserId = enrollment.StudentUserId,
                EnrollmentId = enrollment.Id,
                CertificateNumber = $"HMN-{_jalali.ToJalaliParts(_jalali.TehranToday).Year}-{RandomNumberGenerator.GetInt32(100000, 999999)}",
                VerificationToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant(),
                StudentFullNameSnapshot = enrollment.Student.FullName,
                TeacherFullNameSnapshot = enrollment.Teacher.FullName,
                InstrumentNameSnapshot = enrollment.Instrument.Name,
                CourseTitleSnapshot = enrollment.PlanTitleSnapshot,
                CompletedSessionsSnapshot = completedCount,
                CourseStartDateGregorian = enrollment.StartDateGregorian,
                CourseCompletionDateGregorian = enrollment.EndDateGregorian ?? _jalali.TehranToday,
                IssuedAtUtc = _jalali.UtcNow
            };
            _db.LearningCertificates.Add(cert);
            await _db.SaveChangesAsync(ct);
        }

        var verifyUrl = $"{siteBaseUrl.TrimEnd('/')}/certificates/verify/{cert.VerificationToken}";
        var qrPng = GenerateQrPng(verifyUrl);
        var pdfBytes = RenderPdf(cert, qrPng);

        return ServiceResult<(LearningCertificate, byte[])>.Ok((cert, pdfBytes), "گواهی‌نامه با موفقیت صادر شد.");
    }

    private byte[] RenderPdf(LearningCertificate cert, byte[] qrPng)
    {
        var startJalali = _jalali.FormatDate(cert.CourseStartDateGregorian);
        var endJalali = _jalali.FormatDate(cert.CourseCompletionDateGregorian);
        var issueJalali = _jalali.FormatDate(cert.IssuedAtUtc);

        var doc = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4.Landscape());
                page.Margin(36);
                page.PageColor("#F7F3EE");
                page.ContentFromRightToLeft();
                page.DefaultTextStyle(x => x.FontFamily("Vazirmatn").FontSize(13).FontColor("#3D2415"));

                page.Content()
                    .Border(3).BorderColor("#6F422D")
                    .Padding(24)
                    .Column(col =>
                    {
                        col.Spacing(14);
                        col.Item().AlignCenter().Text("آموزشگاه تخصصی موسیقی هم‌نوا").FontSize(26).Bold().FontColor("#2A6E6F");
                        col.Item().AlignCenter().Text("گواهی‌نامه پایان دوره آموزشی").FontSize(18).SemiBold().FontColor("#6F422D");
                        col.Item().LineHorizontal(1.5f).LineColor("#C8B3A0");

                        col.Item().PaddingTop(10).AlignCenter().Text(text =>
                        {
                            text.Span("بدینوسیله گواهی می‌شود هنرجو ");
                            text.Span(cert.StudentFullNameSnapshot).Bold().FontSize(16).FontColor("#2A6E6F");
                            text.Span(" دوره «");
                            text.Span(cert.CourseTitleSnapshot).Bold();
                            text.Span("» در رشته ساز ");
                            text.Span(cert.InstrumentNameSnapshot).Bold().FontColor("#885F43");
                            text.Span(" را تحت نظر استاد ");
                            text.Span(cert.TeacherFullNameSnapshot).Bold();
                            text.Span(" با موفقیت به پایان رسانده است.");
                        });

                        col.Item().PaddingTop(8).Table(table =>
                        {
                            table.ColumnsDefinition(c =>
                            {
                                c.RelativeColumn();
                                c.RelativeColumn();
                                c.RelativeColumn();
                                c.RelativeColumn();
                            });

                            table.Cell().Padding(6).Text($"تاریخ شروع: {startJalali}");
                            table.Cell().Padding(6).Text($"تاریخ اتمام: {endJalali}");
                            table.Cell().Padding(6).Text($"تعداد جلسات: {_jalali.ToPersianDigits(cert.CompletedSessionsSnapshot.ToString())}");
                            table.Cell().Padding(6).Text($"تاریخ صدور: {issueJalali}");
                        });

                        col.Item().PaddingTop(24).Row(row =>
                        {
                            row.RelativeItem().Column(c =>
                            {
                                c.Item().Text($"شماره گواهی‌نامه: {cert.CertificateNumber}").FontSize(11).FontColor("#6F422D");
                                c.Item().Text("جهت استعلام اصالت، کد QR را اسکن کنید.").FontSize(10).FontColor("#885F43");
                            });
                            row.ConstantItem(92).Height(92).Image(qrPng);
                        });
                    });
            });
        });

        return doc.GeneratePdf();
    }

    private static byte[] GenerateQrPng(string url)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(url, QRCodeGenerator.ECCLevel.M);
        using var qr = new PngByteQRCode(data);
        return qr.GetGraphic(8);
    }
}

public interface IStoryImageService
{
    Task<ServiceResult<byte[]>> GenerateStudentInstagramStoryAsync(
        string studentEnglishSlug,
        string siteBaseUrl,
        string fontFilePath,
        CancellationToken ct = default);
}

/// <summary>
/// Server-side 9:16 (1080x1920) vertical Instagram Story generator using SkiaSharp + HarfBuzz
/// for accurate Persian RTL glyph shaping and Hamnava brand styling.
/// </summary>
public sealed class SkiaStoryImageService : IStoryImageService
{
    private readonly HamnavaDbContext _db;

    public SkiaStoryImageService(HamnavaDbContext db)
    {
        _db = db;
    }

    public async Task<ServiceResult<byte[]>> GenerateStudentInstagramStoryAsync(
        string studentEnglishSlug,
        string siteBaseUrl,
        string fontFilePath,
        CancellationToken ct = default)
    {
        var student = await _db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.PublicSlug == studentEnglishSlug && u.IsPublicProfileVisible, ct);

        if (student is null)
        {
            return ServiceResult<byte[]>.Fail("پورتفولیوی عمومی این هنرجو یافت نشد.", "PORTFOLIO_NOT_FOUND");
        }

        var instrumentName = student.PrimaryInstrumentId.HasValue
            ? await _db.Instruments.AsNoTracking()
                .Where(i => i.Id == student.PrimaryInstrumentId.Value)
                .Select(i => i.Name)
                .FirstOrDefaultAsync(ct) ?? "موسیقی"
            : "موسیقی";

        var template = await _db.StoryTemplates
            .AsNoTracking()
            .OrderByDescending(t => t.IsDefault)
            .FirstOrDefaultAsync(t => t.IsActive, ct);

        const int width = 1080;
        const int height = 1920;

        using var surface = SKSurface.Create(new SKImageInfo(width, height));
        var canvas = surface.Canvas;

        var primary = SKColor.Parse(template?.PrimaryColorHex ?? "#6FA7A8");
        var secondary = SKColor.Parse(template?.SecondaryColorHex ?? "#2A6E6F");
        var beige = SKColor.Parse(template?.AccentColorHex ?? "#C8B3A0");
        var darkBrown = SKColor.Parse("#6F422D");

        // Warm gradient background respecting Hamnava brand palette
        using (var bgPaint = new SKPaint())
        {
            bgPaint.Shader = SKShader.CreateLinearGradient(
                new SKPoint(0, 0),
                new SKPoint(width, height),
                [darkBrown, secondary, primary],
                [0f, 0.55f, 1f],
                SKShaderTileMode.Clamp);
            canvas.DrawRect(0, 0, width, height, bgPaint);
        }

        // Subtle glassmorphic central card
        using (var cardPaint = new SKPaint { Color = new SKColor(255, 250, 245, 36), IsAntialias = true })
        {
            canvas.DrawRoundRect(new SKRoundRect(new SKRect(80, 260, width - 80, height - 240), 48, 48), cardPaint);
        }

        using (var borderPaint = new SKPaint { Color = beige.WithAlpha(160), Style = SKPaintStyle.Stroke, StrokeWidth = 3, IsAntialias = true })
        {
            canvas.DrawRoundRect(new SKRoundRect(new SKRect(80, 260, width - 80, height - 240), 48, 48), borderPaint);
        }

        var typeface = File.Exists(fontFilePath)
            ? SKTypeface.FromFile(fontFilePath)
            : SKTypeface.Default;

        using var shaper = new SKShaper(typeface);

        DrawCenteredShapedText(canvas, shaper, typeface, "آکادمی موسیقی هم‌نوا", 64, width / 2f, 420, SKColors.White);
        DrawCenteredShapedText(canvas, shaper, typeface, student.FullName, 76, width / 2f, 920, SKColors.White);
        DrawCenteredShapedText(canvas, shaper, typeface, $"هنرجوی ساز {instrumentName}", 48, width / 2f, 1040, beige);

        var portfolioUrl = $"{siteBaseUrl.TrimEnd('/')}/student/{student.PublicSlug}";
        DrawCenteredShapedText(canvas, shaper, typeface, portfolioUrl, 34, width / 2f, 1520, SKColors.White.WithAlpha(230));

        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 95);
        return ServiceResult<byte[]>.Ok(data.ToArray(), "تصویر استوری اینستاگرام با موفقیت تولید شد.");
    }

    private static void DrawCenteredShapedText(
        SKCanvas canvas,
        SKShaper shaper,
        SKTypeface typeface,
        string text,
        float fontSize,
        float centerX,
        float baselineY,
        SKColor color)
    {
        using var font = new SKFont(typeface, fontSize);
        using var paint = new SKPaint { Color = color, IsAntialias = true };
        var shaped = shaper.Shape(text, font);
        var x = centerX - (shaped.Width / 2f);
        canvas.DrawShapedText(shaper, text, x, baselineY, font, paint);
    }
}
