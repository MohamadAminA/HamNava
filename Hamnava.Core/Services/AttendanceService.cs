using System.Data;
using Hamnava.Core.Common;
using Hamnava.Core.DateTime;
using Hamnava.Core.Finance;
using Hamnava.Core.Security;
using Hamnava.DataLayer.Context;
using Hamnava.Domain.Constants;
using Hamnava.Domain.Entities.Education;
using Hamnava.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Hamnava.Core.Services;

public interface IAttendanceService
{
    Task<ServiceResult> MarkAttendanceAsync(
        Guid classSessionId,
        ClassSessionStatus newStatus,
        string? notes,
        ActorContext actor,
        CancellationToken ct = default);
    Task<ServiceResult> RequestStudentAuthorizedAbsenceAsync(
        Guid classSessionId,
        string? reason,
        ActorContext studentActor,
        CancellationToken ct = default);
}

public sealed class AttendanceService : IAttendanceService
{
    private readonly HamnavaDbContext _db;
    private readonly IAuthorizationScopeService _authScope;
    private readonly IQuotaService _quotaService;
    private readonly IFinancialService _financialService;
    private readonly ISystemSettingsService _settings;
    private readonly IJalaliDateService _jalali;

    public AttendanceService(
        HamnavaDbContext db,
        IAuthorizationScopeService authScope,
        IQuotaService quotaService,
        IFinancialService financialService,
        ISystemSettingsService settings,
        IJalaliDateService jalali)
    {
        _db = db;
        _authScope = authScope;
        _quotaService = quotaService;
        _financialService = financialService;
        _settings = settings;
        _jalali = jalali;
    }

    public static bool IsWithinAuthorizedAbsenceNoticeWindow(
        System.DateTime sessionStartAtUtc,
        System.DateTime serverNowUtc,
        int minimumNoticeHours)
    {
        var remaining = sessionStartAtUtc - serverNowUtc;
        return remaining >= TimeSpan.FromHours(minimumNoticeHours);
    }

    public async Task<ServiceResult> MarkAttendanceAsync(
        Guid classSessionId,
        ClassSessionStatus newStatus,
        string? notes,
        ActorContext actor,
        CancellationToken ct = default)
    {
        if (!actor.IsAdmin && !actor.IsTeacher)
        {
            return ServiceResult.Fail("فقط مدیر یا مدرس مجاز به ثبت حضور و غیاب است.", "FORBIDDEN");
        }

        if (newStatus is not (ClassSessionStatus.Present or ClassSessionStatus.AuthorizedAbsence or ClassSessionStatus.UnauthorizedAbsence or ClassSessionStatus.Holiday))
        {
            return ServiceResult.Fail("وضعیت حضور و غیاب انتخاب‌شده معتبر نیست.", "INVALID_STATUS");
        }

        await using var tx = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);

        var session = await _db.ClassSessions
            .Include(s => s.Enrollment)
            .FirstOrDefaultAsync(s => s.Id == classSessionId, ct);

        if (session is null)
        {
            return ServiceResult.Fail("جلسه موردنظر یافت نشد.", "SESSION_NOT_FOUND");
        }

        await _authScope.EnsureCanAccessStudentAsync(actor, session.StudentUserId, ct);

        // Idempotent no-op if already in the exact target status
        if (session.Status == newStatus)
        {
            return ServiceResult.Ok("وضعیت حضور و غیاب از قبل ثبت شده است.");
        }

        var previousStatus = session.Status;
        var wasConsumed = previousStatus is ClassSessionStatus.Present or ClassSessionStatus.UnauthorizedAbsence;
        var willConsume = newStatus is ClassSessionStatus.Present or ClassSessionStatus.UnauthorizedAbsence;

        // If previously financially settled and now transitioning to a non-settled state, reverse via compensating ledger
        var reversedSettlement = false;
        if (session.IsFinanciallySettled && !willConsume)
        {
            await _financialService.ReverseSessionSettlementAsync(
                session.Id,
                notes ?? $"تغییر وضعیت از {previousStatus} به {newStatus}",
                actor,
                ct);
            reversedSettlement = true;
        }

        // Update remaining sessions accurately
        if (!wasConsumed && willConsume && session.Enrollment.RemainingSessions > 0)
        {
            session.Enrollment.RemainingSessions--;
        }
        else if (wasConsumed && !willConsume && session.Enrollment.RemainingSessions < session.Enrollment.TotalSessionsSnapshot)
        {
            session.Enrollment.RemainingSessions++;
        }

        session.Status = newStatus;
        session.UpdatedByUserId = actor.UserId;

        var triggeredSettlement = false;
        if (willConsume && !session.IsFinanciallySettled)
        {
            var settlementResult = await _financialService.SettleSessionCommissionAsync(session.Id, actor, ct);
            if (!settlementResult.Succeeded)
            {
                await tx.RollbackAsync(ct);
                return ServiceResult.Fail(settlementResult.Message, settlementResult.ErrorCode);
            }

            triggeredSettlement = true;
        }

        _db.AttendanceRecords.Add(new AttendanceRecord
        {
            ClassSessionId = session.Id,
            PreviousStatus = previousStatus,
            NewStatus = newStatus,
            RecordedByUserId = actor.UserId,
            RecordedAtUtc = _jalali.UtcNow,
            Notes = notes?.Trim(),
            TriggeredFinancialSettlement = triggeredSettlement,
            TriggeredCompensatingReversal = reversedSettlement
        });

        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return ServiceResult.Ok("وضعیت حضور و غیاب با موفقیت ثبت شد.");
    }

    public async Task<ServiceResult> RequestStudentAuthorizedAbsenceAsync(
        Guid classSessionId,
        string? reason,
        ActorContext studentActor,
        CancellationToken ct = default)
    {
        await using var tx = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);

        var session = await _db.ClassSessions
            .FirstOrDefaultAsync(s => s.Id == classSessionId, ct);

        if (session is null)
        {
            return ServiceResult.Fail("جلسه موردنظر یافت نشد.", "SESSION_NOT_FOUND");
        }

        // Strict student ownership check
        if (session.StudentUserId != studentActor.UserId && !studentActor.IsAdmin)
        {
            return ServiceResult.Fail("شما مجاز به ثبت غیبت برای این جلسه نیستید.", "FORBIDDEN");
        }

        if (session.Status != ClassSessionStatus.Scheduled)
        {
            return ServiceResult.Fail("این جلسه قبلاً پردازش شده و قابل ثبت غیبت موجه نیست.", "SESSION_ALREADY_PROCESSED");
        }

        var nowUtc = _jalali.UtcNow;
        if (session.StartAtUtc <= nowUtc)
        {
            return ServiceResult.Fail("امکان ثبت غیبت موجه برای جلسات گذشته وجود ندارد.", "SESSION_IN_PAST");
        }

        // Authoritative server-side 24-hour notice check
        var minNoticeHours = await _settings.GetIntAsync(SystemSettingKeys.QuotaMinimumAbsenceNoticeHours, 24, ct);
        if (!IsWithinAuthorizedAbsenceNoticeWindow(session.StartAtUtc, nowUtc, minNoticeHours))
        {
            return ServiceResult.Fail(
                $"ثبت غیبت موجه تنها تا حداقل {minNoticeHours} ساعت پیش از شروع کلاس امکان‌پذیر است. عدم حضور دیرهنگام به عنوان غیبت غیرموجه محاسبه می‌شود.",
                "NOTICE_PERIOD_VIOLATED");
        }

        var quotaCheck = await _quotaService.EnsureCanConsumeAsync(session.StudentUserId, QuotaUsageType.AuthorizedAbsence, ct);
        if (!quotaCheck.Succeeded)
        {
            return ServiceResult.Fail(quotaCheck.Message, quotaCheck.ErrorCode);
        }

        var previous = session.Status;
        session.Status = ClassSessionStatus.AuthorizedAbsence;
        session.UpdatedByUserId = studentActor.UserId;

        await _quotaService.ConsumeQuotaInternalAsync(
            session.StudentUserId,
            QuotaUsageType.AuthorizedAbsence,
            studentActor.UserId,
            session.Id,
            null,
            string.IsNullOrWhiteSpace(reason) ? "ثبت غیبت موجه توسط هنرجو" : reason.Trim(),
            ct);

        _db.AttendanceRecords.Add(new AttendanceRecord
        {
            ClassSessionId = session.Id,
            PreviousStatus = previous,
            NewStatus = ClassSessionStatus.AuthorizedAbsence,
            RecordedByUserId = studentActor.UserId,
            RecordedAtUtc = nowUtc,
            Notes = string.IsNullOrWhiteSpace(reason) ? "اعلام غیبت موجه توسط هنرجو با رعایت مهلت قانونی" : reason.Trim()
        });

        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return ServiceResult.Ok("غیبت موجه شما با موفقیت ثبت شد. اکنون می‌توانید از بخش جبرانی زمان جایگزین انتخاب کنید.");
    }
}
