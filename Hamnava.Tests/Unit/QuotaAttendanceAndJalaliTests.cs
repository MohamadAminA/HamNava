using FluentAssertions;
using Hamnava.Core.DateTime;
using Hamnava.Core.Media;
using Hamnava.Core.Messaging;
using Hamnava.Core.Services;
using Xunit;

namespace Hamnava.Tests.Unit;

public sealed class QuotaAttendanceAndJalaliTests
{
    [Fact]
    public void AuthorizedAbsenceNoticeWindow_Exact24HoursPasses_LessThan24HoursFails()
    {
        var sessionStartUtc = new DateTime(2026, 5, 10, 14, 0, 0, DateTimeKind.Utc);
        var exact24HoursBefore = sessionStartUtc.AddHours(-24);
        var twentyThreeHours59MinBefore = sessionStartUtc.AddHours(-24).AddMinutes(1);

        AttendanceService.IsWithinAuthorizedAbsenceNoticeWindow(sessionStartUtc, exact24HoursBefore, 24)
            .Should().BeTrue("exactly 24 hours before session start satisfies minimum 24h rule");

        AttendanceService.IsWithinAuthorizedAbsenceNoticeWindow(sessionStartUtc, twentyThreeHours59MinBefore, 24)
            .Should().BeFalse("23h59m before session start violates minimum 24h notice rule");
    }

    [Fact]
    public void Fixed60DayQuotaPeriod_RollsOverIndependentlyOfCourseRenewal()
    {
        var quotaOrigin = new DateOnly(2026, 1, 1);

        var day0 = QuotaService.CalculateFixedPeriod(quotaOrigin, new DateOnly(2026, 1, 1), 60);
        day0.PeriodIndex.Should().Be(0);
        day0.StartDate.Should().Be(new DateOnly(2026, 1, 1));
        day0.EndDate.Should().Be(new DateOnly(2026, 3, 1));

        // Day 59 is still period 0
        var day59 = QuotaService.CalculateFixedPeriod(quotaOrigin, quotaOrigin.AddDays(59), 60);
        day59.PeriodIndex.Should().Be(0);

        // Day 60 starts fresh period 1 automatically without resetting on enrollment renewal
        var day60 = QuotaService.CalculateFixedPeriod(quotaOrigin, quotaOrigin.AddDays(60), 60);
        day60.PeriodIndex.Should().Be(1);
        day60.StartDate.Should().Be(quotaOrigin.AddDays(60));
        day60.EndDate.Should().Be(quotaOrigin.AddDays(119));
    }

    [Fact]
    public void JalaliDateService_RoundTripAndLeapYearEsfand30BirthdayMatch()
    {
        var jalali = new JalaliDateService();

        var parsed = jalali.ParseJalaliDate("1404/01/01");
        parsed.Should().Be(new DateOnly(2025, 3, 21));
        jalali.FormatDate(parsed, usePersianDigits: false).Should().Be("1404/01/01");

        // 1403 is a Persian leap year (Esfand has 30 days: 2025-03-20); 1404 is non-leap (Esfand has 29 days: 2026-03-20)
        var bornEsfand30 = jalali.FromJalaliParts(1403, 12, 30);
        var esfand29NextYear = jalali.FromJalaliParts(1404, 12, 29);

        jalali.IsBirthdayMatch(bornEsfand30, esfand29NextYear)
            .Should().BeTrue("students born on leap Esfand 30 must still receive birthday discount on Esfand 29 in non-leap years");
    }

    [Fact]
    public void SmsTemplateRenderer_BlocksDisallowedPlaceholdersAndSubstitutesAllowedTokens()
    {
        var allowed = SmsTemplateRenderer.Render(
            "سلام {StudentName}، کلاس شما در {Date} است.",
            "StudentName,Date",
            new Dictionary<string, string> { ["StudentName"] = "آرش", ["Date"] = "۱۴۰۴/۰۲/۰۱" });

        allowed.Succeeded.Should().BeTrue();
        allowed.Data.Should().Be("سلام آرش، کلاس شما در ۱۴۰۴/۰۲/۰۱ است.");

        var blocked = SmsTemplateRenderer.Render(
            "سلام {StudentName} {InjectedToken}",
            "StudentName,Date",
            new Dictionary<string, string> { ["StudentName"] = "آرش" });

        blocked.Succeeded.Should().BeFalse();
        blocked.ErrorCode.Should().Be("INVALID_PLACEHOLDER");
    }

    [Fact]
    public void MediaSignatureValidator_AcceptsValidMagicBytesAndRejectsSpoofedFiles()
    {
        byte[] validJpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10];
        byte[] validMp4 = [0x00, 0x00, 0x00, 0x18, (byte)'f', (byte)'t', (byte)'y', (byte)'p', (byte)'m', (byte)'p', (byte)'4', (byte)'2'];
        byte[] fakeExe = [(byte)'M', (byte)'Z', 0x90, 0x00, 0x03, 0x00, 0x00, 0x00];

        MediaSignatureValidator.ValidateMagicBytes(validJpeg, ".jpg").Should().BeTrue();
        MediaSignatureValidator.ValidateMagicBytes(validMp4, ".mp4").Should().BeTrue();
        MediaSignatureValidator.ValidateMagicBytes(fakeExe, ".jpg").Should().BeFalse();
        MediaSignatureValidator.ValidateMagicBytes(fakeExe, ".mp4").Should().BeFalse();
    }
}
