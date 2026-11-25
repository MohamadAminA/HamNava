using System.Text.Json;
using Hamnava.Core.Common;
using Hamnava.Core.DateTime;
using Hamnava.Core.Security;
using Hamnava.DataLayer.Context;
using Hamnava.Domain.Constants;
using Hamnava.Domain.Entities.Education;
using Hamnava.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Hamnava.Core.Services;

public sealed record StudentQuotaSummaryDto(
    Guid PeriodId,
    int PeriodIndex,
    DateOnly PeriodStartGregorian,
    DateOnly PeriodEndGregorian,
    string PeriodStartJalali,
    string PeriodEndJalali,
    int MaxAuthorizedAbsences,
    int UsedAuthorizedAbsences,
    int RemainingAuthorizedAbsences,
    int MaxRescheduleRequests,
    int UsedRescheduleRequests,
    int RemainingRescheduleRequests,
    int MaxMakeupSelections,
    int UsedMakeupSelections,
    int RemainingMakeupSelections);

public interface IQuotaService
{
    Task<StudentQuotaSummaryDto> GetCurrentQuotaSummaryAsync(Guid studentUserId, CancellationToken ct = default);
    Task<ServiceResult> EnsureCanConsumeAsync(Guid studentUserId, QuotaUsageType usageType, CancellationToken ct = default);
    Task ConsumeQuotaInternalAsync(
        Guid studentUserId,
        QuotaUsageType usageType,
        Guid performedByUserId,
        Guid? relatedSessionId,
        Guid? relatedRequestId,
        string reason,
        CancellationToken ct = default);
    Task<ServiceResult<StudentQuotaSummaryDto>> ResetStudentQuotaAsync(
        Guid studentUserId,
        string reason,
        ActorContext actor,
        CancellationToken ct = default);
}

/// <summary>
/// Authoritative fixed-period quota engine (initial default: 60-day periods calculated from
/// User.QuotaStartDateGregorian, independent of course enrollment renewal).
/// Pure period calculation logic is exposed statically for deterministic unit testing.
/// </summary>
public sealed class QuotaService : IQuotaService
{
    private readonly HamnavaDbContext _db;
    private readonly ISystemSettingsService _settings;
    private readonly IAuthorizationScopeService _authScope;
    private readonly IJalaliDateService _jalali;

    public QuotaService(
        HamnavaDbContext db,
        ISystemSettingsService settings,
        IAuthorizationScopeService authScope,
        IJalaliDateService jalali)
    {
        _db = db;
        _settings = settings;
        _authScope = authScope;
        _jalali = jalali;
    }

    public static (int PeriodIndex, DateOnly StartDate, DateOnly EndDate) CalculateFixedPeriod(
        DateOnly quotaStartDate,
        DateOnly referenceDate,
        int periodLengthDays)
    {
        if (periodLengthDays <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(periodLengthDays));
        }

        var elapsedDays = Math.Max(0, referenceDate.DayNumber - quotaStartDate.DayNumber);
        var periodIndex = elapsedDays / periodLengthDays;
        var start = quotaStartDate.AddDays(periodIndex * periodLengthDays);
        var end = start.AddDays(periodLengthDays - 1);
        return (periodIndex, start, end);
    }

    public async Task<StudentQuotaSummaryDto> GetCurrentQuotaSummaryAsync(Guid studentUserId, CancellationToken ct = default)
    {
        var period = await GetOrCreateCurrentPeriodAsync(studentUserId, ct);
        return await BuildSummaryAsync(period, ct);
    }

    public async Task<ServiceResult> EnsureCanConsumeAsync(Guid studentUserId, QuotaUsageType usageType, CancellationToken ct = default)
    {
        var summary = await GetCurrentQuotaSummaryAsync(studentUserId, ct);

        return usageType switch
        {
            QuotaUsageType.AuthorizedAbsence when summary.RemainingAuthorizedAbsences <= 0 =>
                ServiceResult.Fail("سهمیه غیبت موجه شما در این دوره تکمیل شده است.", "ABSENCE_QUOTA_EXHAUSTED"),
            QuotaUsageType.RescheduleRequest when summary.RemainingRescheduleRequests <= 0 =>
                ServiceResult.Fail("سهمیه درخواست تغییر زمان کلاس شما در این دوره تکمیل شده است.", "RESCHEDULE_QUOTA_EXHAUSTED"),
            QuotaUsageType.MakeupSelection when summary.RemainingMakeupSelections <= 0 =>
                ServiceResult.Fail("سهمیه انتخاب جلسه جبرانی شما در این دوره تکمیل شده است.", "MAKEUP_QUOTA_EXHAUSTED"),
            _ => ServiceResult.Ok()
        };
    }

    public async Task ConsumeQuotaInternalAsync(
        Guid studentUserId,
        QuotaUsageType usageType,
        Guid performedByUserId,
        Guid? relatedSessionId,
        Guid? relatedRequestId,
        string reason,
        CancellationToken ct = default)
    {
        var period = await GetOrCreateCurrentPeriodAsync(studentUserId, ct);
        _db.StudentQuotaUsages.Add(new StudentQuotaUsage
        {
            StudentQuotaPeriodId = period.Id,
            StudentUserId = studentUserId,
            UsageType = usageType,
            Delta = 1,
            RelatedSessionId = relatedSessionId,
            RelatedRequestId = relatedRequestId,
            PerformedByUserId = performedByUserId,
            Reason = reason
        });
    }

    public async Task<ServiceResult<StudentQuotaSummaryDto>> ResetStudentQuotaAsync(
        Guid studentUserId,
        string reason,
        ActorContext actor,
        CancellationToken ct = default)
    {
        await _authScope.EnsureCanAccessStudentAsync(actor, studentUserId, ct);

        if (!actor.IsAdmin)
        {
            var teacherAllowed = await _settings.GetBoolAsync(SystemSettingKeys.TeacherCanResetOwnStudentQuotas, true, ct);
            if (!actor.IsTeacher || !teacherAllowed)
            {
                return ServiceResult<StudentQuotaSummaryDto>.Fail("شما مجاز به بازنشانی سهمیه هنرجو نیستید.", "FORBIDDEN");
            }
        }

        var period = await GetOrCreateCurrentPeriodAsync(studentUserId, ct);
        var before = await BuildSummaryAsync(period, ct);

        // Preserve historical quota usage records; add compensating negative-delta administrative reset records
        var resetReason = string.IsNullOrWhiteSpace(reason) ? "بازنشانی مدیریتی سهمیه" : reason.Trim();

        if (before.UsedAuthorizedAbsences > 0)
        {
            _db.StudentQuotaUsages.Add(new StudentQuotaUsage
            {
                StudentQuotaPeriodId = period.Id,
                StudentUserId = studentUserId,
                UsageType = QuotaUsageType.AuthorizedAbsence,
                Delta = -before.UsedAuthorizedAbsences,
                PerformedByUserId = actor.UserId,
                PreviousStateSnapshotJson = JsonSerializer.Serialize(before),
                Reason = resetReason
            });
        }

        if (before.UsedRescheduleRequests > 0)
        {
            _db.StudentQuotaUsages.Add(new StudentQuotaUsage
            {
                StudentQuotaPeriodId = period.Id,
                StudentUserId = studentUserId,
                UsageType = QuotaUsageType.RescheduleRequest,
                Delta = -before.UsedRescheduleRequests,
                PerformedByUserId = actor.UserId,
                PreviousStateSnapshotJson = JsonSerializer.Serialize(before),
                Reason = resetReason
            });
        }

        if (before.UsedMakeupSelections > 0)
        {
            _db.StudentQuotaUsages.Add(new StudentQuotaUsage
            {
                StudentQuotaPeriodId = period.Id,
                StudentUserId = studentUserId,
                UsageType = QuotaUsageType.MakeupSelection,
                Delta = -before.UsedMakeupSelections,
                PerformedByUserId = actor.UserId,
                PreviousStateSnapshotJson = JsonSerializer.Serialize(before),
                Reason = resetReason
            });
        }

        _db.StudentQuotaUsages.Add(new StudentQuotaUsage
        {
            StudentQuotaPeriodId = period.Id,
            StudentUserId = studentUserId,
            UsageType = QuotaUsageType.AdministrativeReset,
            Delta = 0,
            PerformedByUserId = actor.UserId,
            PreviousStateSnapshotJson = JsonSerializer.Serialize(before),
            Reason = resetReason
        });

        await _db.SaveChangesAsync(ct);

        var after = await BuildSummaryAsync(period, ct);
        await _authScope.WriteAuditLogAsync(actor, "ResetQuota", nameof(StudentQuotaPeriod), period.Id.ToString(), before, after, ct);

        return ServiceResult<StudentQuotaSummaryDto>.Ok(after, "سهمیه دوره فعلی هنرجو با موفقیت بازنشانی شد.");
    }

    private async Task<StudentQuotaPeriod> GetOrCreateCurrentPeriodAsync(Guid studentUserId, CancellationToken ct)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == studentUserId, ct)
            ?? throw new KeyNotFoundException("هنرجو یافت نشد.");

        var today = _jalali.TehranToday;
        if (!user.QuotaStartDateGregorian.HasValue)
        {
            user.QuotaStartDateGregorian = today;
            await _db.SaveChangesAsync(ct);
        }

        var periodDays = await _settings.GetIntAsync(SystemSettingKeys.QuotaPeriodDays, 60, ct);
        var (periodIndex, start, end) = CalculateFixedPeriod(user.QuotaStartDateGregorian.Value, today, periodDays);

        var period = await _db.StudentQuotaPeriods
            .FirstOrDefaultAsync(p => p.StudentUserId == studentUserId && p.PeriodIndex == periodIndex, ct);

        if (period is not null)
        {
            return period;
        }

        period = new StudentQuotaPeriod
        {
            StudentUserId = studentUserId,
            PeriodIndex = periodIndex,
            PeriodStartDateGregorian = start,
            PeriodEndDateGregorian = end,
            MaxAuthorizedAbsencesSnapshot = await _settings.GetIntAsync(SystemSettingKeys.QuotaMaxAuthorizedAbsences, 1, ct),
            MaxRescheduleRequestsSnapshot = await _settings.GetIntAsync(SystemSettingKeys.QuotaMaxRescheduleRequests, 2, ct),
            MaxMakeupSelectionsSnapshot = await _settings.GetIntAsync(SystemSettingKeys.QuotaMaxMakeupSelections, 2, ct)
        };

        _db.StudentQuotaPeriods.Add(period);
        await _db.SaveChangesAsync(ct);
        return period;
    }

    private async Task<StudentQuotaSummaryDto> BuildSummaryAsync(StudentQuotaPeriod period, CancellationToken ct)
    {
        var usages = await _db.StudentQuotaUsages
            .AsNoTracking()
            .Where(u => u.StudentQuotaPeriodId == period.Id)
            .ToListAsync(ct);

        var usedAbsences = Math.Max(0, usages.Where(u => u.UsageType == QuotaUsageType.AuthorizedAbsence).Sum(u => u.Delta));
        var usedReschedules = Math.Max(0, usages.Where(u => u.UsageType == QuotaUsageType.RescheduleRequest).Sum(u => u.Delta));
        var usedMakeups = Math.Max(0, usages.Where(u => u.UsageType == QuotaUsageType.MakeupSelection).Sum(u => u.Delta));

        return new StudentQuotaSummaryDto(
            period.Id,
            period.PeriodIndex,
            period.PeriodStartDateGregorian,
            period.PeriodEndDateGregorian,
            _jalali.FormatDate(period.PeriodStartDateGregorian),
            _jalali.FormatDate(period.PeriodEndDateGregorian),
            period.MaxAuthorizedAbsencesSnapshot,
            usedAbsences,
            Math.Max(0, period.MaxAuthorizedAbsencesSnapshot - usedAbsences),
            period.MaxRescheduleRequestsSnapshot,
            usedReschedules,
            Math.Max(0, period.MaxRescheduleRequestsSnapshot - usedReschedules),
            period.MaxMakeupSelectionsSnapshot,
            usedMakeups,
            Math.Max(0, period.MaxMakeupSelectionsSnapshot - usedMakeups));
    }
}
