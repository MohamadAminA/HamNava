using System.Data;
using Hamnava.Core.Common;
using Hamnava.Core.DateTime;
using Hamnava.Core.Security;
using Hamnava.Core.Services;
using Hamnava.DataLayer.Context;
using Hamnava.Domain.Constants;
using Hamnava.Domain.Entities.Education;
using Hamnava.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Hamnava.Core.Scheduling;

public sealed record ScheduleValidationRequest(
    Guid TeacherUserId,
    Guid StudentUserId,
    Guid RoomId,
    Guid InstrumentId,
    System.DateTime StartAtUtc,
    System.DateTime EndAtUtc,
    Guid? ExcludeSessionId = null);

public sealed record RecurringSessionPlanRequest(
    Guid EnrollmentId,
    System.DateTime FirstSessionStartAtUtc,
    bool SkipAcademicHolidays = true);

public sealed record DragDropMoveRequest(
    Guid SessionId,
    System.DateTime NewStartAtUtc,
    Guid? NewRoomId = null,
    string? Reason = null);

public sealed record ManualMakeupRequest(
    Guid StudentUserId,
    Guid OriginalSessionId,
    System.DateTime NewStartAtUtc,
    int DurationMinutes,
    Guid RoomId,
    Guid InstrumentId,
    string? Reason = null);

public sealed record CalendarEventDto(
    Guid SessionId,
    Guid EnrollmentId,
    Guid StudentUserId,
    string StudentFullName,
    Guid TeacherUserId,
    string TeacherFullName,
    Guid InstrumentId,
    string InstrumentName,
    Guid RoomId,
    string RoomName,
    int SessionNumber,
    ClassSessionType SessionType,
    ClassSessionStatus Status,
    System.DateTime StartAtUtc,
    System.DateTime EndAtUtc,
    string StartJalaliDate,
    string StartTimeTehran,
    string EndTimeTehran,
    int DurationMinutes);

public interface ISchedulingService
{
    Task<ServiceResult> ValidateSlotAsync(ScheduleValidationRequest request, CancellationToken ct = default);
    Task<ServiceResult<IReadOnlyList<ClassSession>>> GenerateRecurringSessionsForEnrollmentAsync(
        RecurringSessionPlanRequest request,
        ActorContext actor,
        CancellationToken ct = default);
    Task<ServiceResult<CalendarEventDto>> MoveSessionDragDropAsync(
        DragDropMoveRequest request,
        ActorContext actor,
        CancellationToken ct = default);
    Task<ServiceResult<ClassSession>> CreateManualMakeupSessionAsync(
        ManualMakeupRequest request,
        ActorContext actor,
        CancellationToken ct = default);
    Task<ServiceResult<RescheduleRequest>> SubmitStudentRescheduleRequestAsync(
        Guid sessionId,
        Guid targetTimeSlotId,
        string? studentNote,
        ActorContext actor,
        CancellationToken ct = default);
    Task<ServiceResult<ClassSession>> SelectStudentMakeupSlotAsync(
        Guid originalSessionId,
        Guid targetTimeSlotId,
        ActorContext actor,
        CancellationToken ct = default);
    Task<IReadOnlyList<CalendarEventDto>> GetCalendarEventsAsync(
        ActorContext actor,
        System.DateTime rangeStartUtc,
        System.DateTime rangeEndUtc,
        Guid? teacherIdFilter = null,
        Guid? studentIdFilter = null,
        Guid? roomIdFilter = null,
        Guid? instrumentIdFilter = null,
        CancellationToken ct = default);
}

/// <summary>
/// Single authoritative scheduling & conflict-prevention engine used across Admin, Teacher,
/// Student, Enrollment recurring generation, Make-up booking, and Calendar Drag & Drop.
/// </summary>
public sealed class SchedulingService : ISchedulingService
{
    private static readonly ClassSessionStatus[] BlockingStatuses =
    [
        ClassSessionStatus.Scheduled,
        ClassSessionStatus.Present,
        ClassSessionStatus.UnauthorizedAbsence
    ];

    private readonly HamnavaDbContext _db;
    private readonly IAuthorizationScopeService _authScope;
    private readonly IQuotaService _quotaService;
    private readonly ISystemSettingsService _settings;
    private readonly IJalaliDateService _jalali;

    public SchedulingService(
        HamnavaDbContext db,
        IAuthorizationScopeService authScope,
        IQuotaService quotaService,
        ISystemSettingsService settings,
        IJalaliDateService jalali)
    {
        _db = db;
        _authScope = authScope;
        _quotaService = quotaService;
        _settings = settings;
        _jalali = jalali;
    }

    public async Task<ServiceResult> ValidateSlotAsync(ScheduleValidationRequest request, CancellationToken ct = default)
    {
        if (request.EndAtUtc <= request.StartAtUtc)
        {
            return ServiceResult.Fail("زمان پایان کلاس باید پس از زمان شروع باشد.", "INVALID_TIME_RANGE");
        }

        var room = await _db.Rooms
            .AsNoTracking()
            .Include(r => r.SupportedInstruments)
            .FirstOrDefaultAsync(r => r.Id == request.RoomId && r.IsActive, ct);

        if (room is null)
        {
            return ServiceResult.Fail("کلاس انتخاب‌شده یافت نشد یا غیرفعال است.", "ROOM_NOT_FOUND");
        }

        // 1. Supported instrument compatibility (M:N)
        var supportsInstrument = room.SupportedInstruments.Any(si => si.InstrumentId == request.InstrumentId);
        if (!supportsInstrument)
        {
            return ServiceResult.Fail($"کلاس «{room.Name}» از ساز انتخاب‌شده پشتیبانی نمی‌کند.", "INSTRUMENT_ROOM_INCOMPATIBLE");
        }

        // 2. Teacher conflict (same teacher cannot teach overlapping slots unless co-teaching inside room capacity?
        // Standard rule: one teacher cannot have overlapping lessons at different times/rooms.)
        var teacherConflict = await _db.ClassSessions
            .AsNoTracking()
            .AnyAsync(s =>
                s.TeacherUserId == request.TeacherUserId &&
                s.Id != request.ExcludeSessionId &&
                BlockingStatuses.Contains(s.Status) &&
                s.StartAtUtc < request.EndAtUtc &&
                s.EndAtUtc > request.StartAtUtc, ct);

        if (teacherConflict)
        {
            return ServiceResult.Fail("مدرس در این بازه زمانی کلاس دیگری دارد.", "TEACHER_CONFLICT");
        }

        // 3. Student conflict
        var studentConflict = await _db.ClassSessions
            .AsNoTracking()
            .AnyAsync(s =>
                s.StudentUserId == request.StudentUserId &&
                s.Id != request.ExcludeSessionId &&
                BlockingStatuses.Contains(s.Status) &&
                s.StartAtUtc < request.EndAtUtc &&
                s.EndAtUtc > request.StartAtUtc, ct);

        if (studentConflict)
        {
            return ServiceResult.Fail("هنرجو در این بازه زمانی جلسه دیگری دارد.", "STUDENT_CONFLICT");
        }

        // 4. Dynamic room capacity enforcement (NEVER hardcoded to 2 — reads room.Capacity from SQL Server)
        var overlappingInRoom = await _db.ClassSessions
            .AsNoTracking()
            .CountAsync(s =>
                s.RoomId == request.RoomId &&
                s.Id != request.ExcludeSessionId &&
                BlockingStatuses.Contains(s.Status) &&
                s.StartAtUtc < request.EndAtUtc &&
                s.EndAtUtc > request.StartAtUtc, ct);

        if (overlappingInRoom >= room.Capacity)
        {
            return ServiceResult.Fail(
                $"ظرفیت کلاس «{room.Name}» ({room.Capacity} نفر) در این ساعت تکمیل است.",
                "ROOM_CAPACITY_EXCEEDED");
        }

        return ServiceResult.Ok();
    }

    public async Task<ServiceResult<IReadOnlyList<ClassSession>>> GenerateRecurringSessionsForEnrollmentAsync(
        RecurringSessionPlanRequest request,
        ActorContext actor,
        CancellationToken ct = default)
    {
        var enrollment = await _db.Enrollments
            .FirstOrDefaultAsync(e => e.Id == request.EnrollmentId, ct);

        if (enrollment is null)
        {
            return ServiceResult<IReadOnlyList<ClassSession>>.Fail("ثبت‌نام موردنظر یافت نشد.", "ENROLLMENT_NOT_FOUND");
        }

        await _authScope.EnsureCanAccessStudentAsync(actor, enrollment.StudentUserId, ct);

        if (enrollment.Status != EnrollmentStatus.Active)
        {
            return ServiceResult<IReadOnlyList<ClassSession>>.Fail("تنها برای ثبت‌نام‌های فعال می‌توان جلسات ایجاد کرد.", "ENROLLMENT_NOT_ACTIVE");
        }

        var existingCount = await _db.ClassSessions.CountAsync(s => s.EnrollmentId == enrollment.Id, ct);
        if (existingCount > 0)
        {
            return ServiceResult<IReadOnlyList<ClassSession>>.Fail("جلسات این ثبت‌نام قبلاً ایجاد شده‌اند.", "SESSIONS_ALREADY_GENERATED");
        }

        await using var tx = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);

        var holidayDates = request.SkipAcademicHolidays
            ? (await _db.AcademicHolidays.AsNoTracking().Select(h => h.HolidayDateGregorian).ToListAsync(ct)).ToHashSet()
            : [];

        var created = new List<ClassSession>(enrollment.TotalSessionsSnapshot);
        var cursorStartUtc = System.DateTime.SpecifyKind(request.FirstSessionStartAtUtc, DateTimeKind.Utc);
        var duration = enrollment.LessonDurationMinutesSnapshot;

        var safetyCounter = 0;
        while (created.Count < enrollment.TotalSessionsSnapshot && safetyCounter < 200)
        {
            safetyCounter++;
            var tehranDate = DateOnly.FromDateTime(_jalali.ToTehranTime(cursorStartUtc));
            if (holidayDates.Contains(tehranDate))
            {
                cursorStartUtc = cursorStartUtc.AddDays(7);
                continue;
            }

            var cursorEndUtc = cursorStartUtc.AddMinutes(duration);
            var validation = await ValidateSlotAsync(new ScheduleValidationRequest(
                enrollment.TeacherUserId,
                enrollment.StudentUserId,
                enrollment.RoomId,
                enrollment.InstrumentId,
                cursorStartUtc,
                cursorEndUtc), ct);

            if (!validation.Succeeded)
            {
                await tx.RollbackAsync(ct);
                var jalaliDate = _jalali.FormatDateTime(cursorStartUtc);
                return ServiceResult<IReadOnlyList<ClassSession>>.Fail(
                    $"تداخل در جلسه تاریخ {jalaliDate}: {validation.Message}",
                    validation.ErrorCode);
            }

            created.Add(new ClassSession
            {
                EnrollmentId = enrollment.Id,
                StudentUserId = enrollment.StudentUserId,
                TeacherUserId = enrollment.TeacherUserId,
                InstrumentId = enrollment.InstrumentId,
                RoomId = enrollment.RoomId,
                SessionNumber = created.Count + 1,
                SessionType = ClassSessionType.Regular,
                Status = ClassSessionStatus.Scheduled,
                StartAtUtc = cursorStartUtc,
                EndAtUtc = cursorEndUtc,
                DurationMinutes = duration,
                CreatedByUserId = actor.UserId
            });

            cursorStartUtc = cursorStartUtc.AddDays(7);
        }

        if (created.Count != enrollment.TotalSessionsSnapshot)
        {
            await tx.RollbackAsync(ct);
            return ServiceResult<IReadOnlyList<ClassSession>>.Fail("امکان تولید تمام جلسات دوره وجود نداشت.", "GENERATION_INCOMPLETE");
        }

        _db.ClassSessions.AddRange(created);
        enrollment.EndDateGregorian = DateOnly.FromDateTime(_jalali.ToTehranTime(created[^1].EndAtUtc));
        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return ServiceResult<IReadOnlyList<ClassSession>>.Ok(created, "برنامه جلسات دوره با موفقیت ایجاد شد.");
    }

    public async Task<ServiceResult<CalendarEventDto>> MoveSessionDragDropAsync(
        DragDropMoveRequest request,
        ActorContext actor,
        CancellationToken ct = default)
    {
        if (!actor.IsAdmin && !actor.IsTeacher)
        {
            return ServiceResult<CalendarEventDto>.Fail("فقط مدیر یا مدرس مجاز به جابجایی مستقیم در تقویم است.", "FORBIDDEN");
        }

        await using var tx = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);

        var session = await _db.ClassSessions
            .Include(s => s.Student)
            .Include(s => s.Teacher)
            .Include(s => s.Instrument)
            .Include(s => s.Room)
            .FirstOrDefaultAsync(s => s.Id == request.SessionId, ct);

        if (session is null)
        {
            return ServiceResult<CalendarEventDto>.Fail("جلسه موردنظر یافت نشد.", "SESSION_NOT_FOUND");
        }

        await _authScope.EnsureCanAccessStudentAsync(actor, session.StudentUserId, ct);

        if (session.Status != ClassSessionStatus.Scheduled)
        {
            return ServiceResult<CalendarEventDto>.Fail("تنها جلسات برنامه‌ریزی‌شده قابل جابجایی هستند.", "SESSION_LOCKED");
        }

        var targetRoomId = request.NewRoomId ?? session.RoomId;
        var newStartUtc = System.DateTime.SpecifyKind(request.NewStartAtUtc, DateTimeKind.Utc);
        var newEndUtc = newStartUtc.AddMinutes(session.DurationMinutes);

        var validation = await ValidateSlotAsync(new ScheduleValidationRequest(
            session.TeacherUserId,
            session.StudentUserId,
            targetRoomId,
            session.InstrumentId,
            newStartUtc,
            newEndUtc,
            ExcludeSessionId: session.Id), ct);

        if (!validation.Succeeded)
        {
            await tx.RollbackAsync(ct);
            return ServiceResult<CalendarEventDto>.Fail(validation.Message, validation.ErrorCode);
        }

        _db.SessionScheduleHistories.Add(new SessionScheduleHistory
        {
            ClassSessionId = session.Id,
            OriginalStartAtUtc = session.StartAtUtc,
            OriginalEndAtUtc = session.EndAtUtc,
            OriginalRoomId = session.RoomId,
            NewStartAtUtc = newStartUtc,
            NewEndAtUtc = newEndUtc,
            NewRoomId = targetRoomId,
            ChangedByUserId = actor.UserId,
            ChangeReason = string.IsNullOrWhiteSpace(request.Reason) ? "جابجایی دستی در تقویم" : request.Reason.Trim()
        });

        session.StartAtUtc = newStartUtc;
        session.EndAtUtc = newEndUtc;
        session.RoomId = targetRoomId;
        session.UpdatedByUserId = actor.UserId;

        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        var roomName = targetRoomId == session.RoomId
            ? session.Room.Name
            : (await _db.Rooms.AsNoTracking().Where(r => r.Id == targetRoomId).Select(r => r.Name).FirstOrDefaultAsync(ct) ?? session.Room.Name);

        return ServiceResult<CalendarEventDto>.Ok(ToCalendarDto(session, roomName), "زمان جلسه با موفقیت به‌روزرسانی شد.");
    }

    public async Task<ServiceResult<ClassSession>> CreateManualMakeupSessionAsync(
        ManualMakeupRequest request,
        ActorContext actor,
        CancellationToken ct = default)
    {
        if (!actor.IsAdmin && !actor.IsTeacher)
        {
            return ServiceResult<ClassSession>.Fail("فقط مدیر یا مدرس مجاز به ثبت جلسه جبرانی دستی است.", "FORBIDDEN");
        }

        await _authScope.EnsureCanAccessStudentAsync(actor, request.StudentUserId, ct);

        await using var tx = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);

        var original = await _db.ClassSessions
            .FirstOrDefaultAsync(s => s.Id == request.OriginalSessionId && s.StudentUserId == request.StudentUserId, ct);

        if (original is null)
        {
            return ServiceResult<ClassSession>.Fail("جلسه اصلی موردنظر یافت نشد.", "ORIGINAL_SESSION_NOT_FOUND");
        }

        var startUtc = System.DateTime.SpecifyKind(request.NewStartAtUtc, DateTimeKind.Utc);
        var endUtc = startUtc.AddMinutes(request.DurationMinutes);

        var validation = await ValidateSlotAsync(new ScheduleValidationRequest(
            original.TeacherUserId,
            original.StudentUserId,
            request.RoomId,
            request.InstrumentId,
            startUtc,
            endUtc), ct);

        if (!validation.Succeeded)
        {
            await tx.RollbackAsync(ct);
            return ServiceResult<ClassSession>.Fail(validation.Message, validation.ErrorCode);
        }

        var makeup = new ClassSession
        {
            EnrollmentId = original.EnrollmentId,
            StudentUserId = original.StudentUserId,
            TeacherUserId = original.TeacherUserId,
            InstrumentId = request.InstrumentId,
            RoomId = request.RoomId,
            SessionNumber = original.SessionNumber,
            SessionType = ClassSessionType.Makeup,
            Status = ClassSessionStatus.Scheduled,
            StartAtUtc = startUtc,
            EndAtUtc = endUtc,
            DurationMinutes = request.DurationMinutes,
            OriginalSessionId = original.Id,
            CreatedByUserId = actor.UserId
        };

        _db.ClassSessions.Add(makeup);
        _db.MakeupLessonRequests.Add(new MakeupLessonRequest
        {
            StudentUserId = original.StudentUserId,
            OriginalSessionId = original.Id,
            CreatedMakeupSession = makeup,
            Status = RequestWorkflowStatus.Completed,
            IsManualTeacherOverride = true,
            ProcessedByUserId = actor.UserId,
            ProcessedAtUtc = _jalali.UtcNow,
            Reason = request.Reason ?? "ثبت جلسه جبرانی توسط مدرس/مدیر"
        });

        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return ServiceResult<ClassSession>.Ok(makeup, "جلسه جبرانی با موفقیت ثبت شد.");
    }

    public async Task<ServiceResult<RescheduleRequest>> SubmitStudentRescheduleRequestAsync(
        Guid sessionId,
        Guid targetTimeSlotId,
        string? studentNote,
        ActorContext actor,
        CancellationToken ct = default)
    {
        await using var tx = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);

        var session = await _db.ClassSessions.FirstOrDefaultAsync(s => s.Id == sessionId, ct);
        if (session is null)
        {
            return ServiceResult<RescheduleRequest>.Fail("جلسه موردنظر یافت نشد.", "SESSION_NOT_FOUND");
        }

        await _authScope.EnsureCanAccessStudentAsync(actor, session.StudentUserId, ct);

        if (session.Status != ClassSessionStatus.Scheduled || session.StartAtUtc <= _jalali.UtcNow)
        {
            return ServiceResult<RescheduleRequest>.Fail("این جلسه قابل درخواست تغییر زمان نیست.", "SESSION_NOT_ELIGIBLE");
        }

        var slot = await _db.TeacherTimeSlots.FirstOrDefaultAsync(t =>
            t.Id == targetTimeSlotId &&
            t.TeacherUserId == session.TeacherUserId &&
            t.IsActive &&
            !t.IsClosed &&
            (t.AvailabilityType == AvailabilitySlotType.Both || t.AvailabilityType == AvailabilitySlotType.RescheduleOnly), ct);

        if (slot is null || slot.StartAtUtc <= _jalali.UtcNow)
        {
            return ServiceResult<RescheduleRequest>.Fail("بازه زمانی انتخاب‌شده برای تغییر زمان در دسترس نیست.", "SLOT_UNAVAILABLE");
        }

        if ((slot.EndAtUtc - slot.StartAtUtc).TotalMinutes < session.DurationMinutes)
        {
            return ServiceResult<RescheduleRequest>.Fail("مدت زمان این بازه با مدت جلسه کلاس شما همخوانی ندارد.", "SLOT_DURATION_MISMATCH");
        }

        var quotaCheck = await _quotaService.EnsureCanConsumeAsync(session.StudentUserId, QuotaUsageType.RescheduleRequest, ct);
        if (!quotaCheck.Succeeded)
        {
            return ServiceResult<RescheduleRequest>.Fail(quotaCheck.Message, quotaCheck.ErrorCode);
        }

        var requestedEndUtc = slot.StartAtUtc.AddMinutes(session.DurationMinutes);
        var conflictCheck = await ValidateSlotAsync(new ScheduleValidationRequest(
            session.TeacherUserId,
            session.StudentUserId,
            slot.RoomId,
            session.InstrumentId,
            slot.StartAtUtc,
            requestedEndUtc,
            ExcludeSessionId: session.Id), ct);

        if (!conflictCheck.Succeeded)
        {
            await tx.RollbackAsync(ct);
            return ServiceResult<RescheduleRequest>.Fail(conflictCheck.Message, conflictCheck.ErrorCode);
        }

        var requiresApproval = await _settings.GetBoolAsync(SystemSettingKeys.RescheduleRequiresTeacherApproval, true, ct);

        var req = new RescheduleRequest
        {
            StudentUserId = session.StudentUserId,
            ClassSessionId = session.Id,
            TargetTimeSlotId = slot.Id,
            OriginalStartAtUtc = session.StartAtUtc,
            RequestedStartAtUtc = slot.StartAtUtc,
            RequestedEndAtUtc = requestedEndUtc,
            Status = requiresApproval ? RequestWorkflowStatus.Pending : RequestWorkflowStatus.Approved,
            StudentNote = studentNote?.Trim()
        };

        _db.RescheduleRequests.Add(req);
        await _quotaService.ConsumeQuotaInternalAsync(
            session.StudentUserId,
            QuotaUsageType.RescheduleRequest,
            actor.UserId,
            session.Id,
            req.Id,
            "درخواست تغییر زمان جلسه توسط هنرجو",
            ct);

        if (!requiresApproval)
        {
            _db.SessionScheduleHistories.Add(new SessionScheduleHistory
            {
                ClassSessionId = session.Id,
                OriginalStartAtUtc = session.StartAtUtc,
                OriginalEndAtUtc = session.EndAtUtc,
                OriginalRoomId = session.RoomId,
                NewStartAtUtc = slot.StartAtUtc,
                NewEndAtUtc = requestedEndUtc,
                NewRoomId = slot.RoomId,
                ChangedByUserId = actor.UserId,
                ChangeReason = "جابجایی خودکار از سهمیه هنرجو",
                RelatedRequestId = req.Id
            });

            session.StartAtUtc = slot.StartAtUtc;
            session.EndAtUtc = requestedEndUtc;
            session.RoomId = slot.RoomId;
            session.TeacherTimeSlotId = slot.Id;
        }

        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return ServiceResult<RescheduleRequest>.Ok(
            req,
            requiresApproval ? "درخواست تغییر زمان ثبت شد و منتظر تایید مدرس است." : "زمان کلاس با موفقیت تغییر یافت.");
    }

    public async Task<ServiceResult<ClassSession>> SelectStudentMakeupSlotAsync(
        Guid originalSessionId,
        Guid targetTimeSlotId,
        ActorContext actor,
        CancellationToken ct = default)
    {
        await using var tx = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);

        var original = await _db.ClassSessions.FirstOrDefaultAsync(s => s.Id == originalSessionId, ct);
        if (original is null)
        {
            return ServiceResult<ClassSession>.Fail("جلسه اصلی یافت نشد.", "ORIGINAL_SESSION_NOT_FOUND");
        }

        await _authScope.EnsureCanAccessStudentAsync(actor, original.StudentUserId, ct);

        // Eligible only for AuthorizedAbsence or Holiday
        if (original.Status is not (ClassSessionStatus.AuthorizedAbsence or ClassSessionStatus.Holiday))
        {
            return ServiceResult<ClassSession>.Fail("فقط برای جلسات دارای غیبت موجه یا تعطیل رسمی می‌توان جلسه جبرانی انتخاب کرد.", "MAKEUP_NOT_ELIGIBLE");
        }

        // Prevent duplicate make-up selection for the same original session
        var alreadyHasMakeup = await _db.MakeupLessonRequests.AnyAsync(m =>
            m.OriginalSessionId == original.Id &&
            m.Status != RequestWorkflowStatus.Rejected &&
            m.Status != RequestWorkflowStatus.Canceled, ct);

        if (alreadyHasMakeup)
        {
            return ServiceResult<ClassSession>.Fail("برای این جلسه قبلاً جلسه جبرانی ثبت شده است.", "DUPLICATE_MAKEUP");
        }

        var quotaCheck = await _quotaService.EnsureCanConsumeAsync(original.StudentUserId, QuotaUsageType.MakeupSelection, ct);
        if (!quotaCheck.Succeeded)
        {
            return ServiceResult<ClassSession>.Fail(quotaCheck.Message, quotaCheck.ErrorCode);
        }

        var slot = await _db.TeacherTimeSlots.FirstOrDefaultAsync(t =>
            t.Id == targetTimeSlotId &&
            t.TeacherUserId == original.TeacherUserId &&
            t.IsActive &&
            !t.IsClosed &&
            (t.AvailabilityType == AvailabilitySlotType.Both || t.AvailabilityType == AvailabilitySlotType.MakeupOnly), ct);

        if (slot is null || slot.StartAtUtc <= _jalali.UtcNow)
        {
            return ServiceResult<ClassSession>.Fail("بازه زمانی انتخاب‌شده برای کلاس جبرانی فعال نیست.", "SLOT_UNAVAILABLE");
        }

        var endUtc = slot.StartAtUtc.AddMinutes(original.DurationMinutes);
        var conflict = await ValidateSlotAsync(new ScheduleValidationRequest(
            original.TeacherUserId,
            original.StudentUserId,
            slot.RoomId,
            original.InstrumentId,
            slot.StartAtUtc,
            endUtc), ct);

        if (!conflict.Succeeded)
        {
            await tx.RollbackAsync(ct);
            return ServiceResult<ClassSession>.Fail(conflict.Message, conflict.ErrorCode);
        }

        var makeupSession = new ClassSession
        {
            EnrollmentId = original.EnrollmentId,
            StudentUserId = original.StudentUserId,
            TeacherUserId = original.TeacherUserId,
            InstrumentId = original.InstrumentId,
            RoomId = slot.RoomId,
            SessionNumber = original.SessionNumber,
            SessionType = ClassSessionType.Makeup,
            Status = ClassSessionStatus.Scheduled,
            StartAtUtc = slot.StartAtUtc,
            EndAtUtc = endUtc,
            DurationMinutes = original.DurationMinutes,
            OriginalSessionId = original.Id,
            TeacherTimeSlotId = slot.Id,
            CreatedByUserId = actor.UserId
        };

        var makeupRequest = new MakeupLessonRequest
        {
            StudentUserId = original.StudentUserId,
            OriginalSessionId = original.Id,
            SelectedSlotId = slot.Id,
            CreatedMakeupSession = makeupSession,
            Status = RequestWorkflowStatus.Completed,
            ProcessedByUserId = actor.UserId,
            ProcessedAtUtc = _jalali.UtcNow,
            Reason = "انتخاب جلسه جبرانی توسط هنرجو"
        };

        _db.ClassSessions.Add(makeupSession);
        _db.MakeupLessonRequests.Add(makeupRequest);

        await _quotaService.ConsumeQuotaInternalAsync(
            original.StudentUserId,
            QuotaUsageType.MakeupSelection,
            actor.UserId,
            original.Id,
            makeupRequest.Id,
            "انتخاب زمان جلسه جبرانی توسط هنرجو",
            ct);

        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return ServiceResult<ClassSession>.Ok(makeupSession, "جلسه جبرانی با موفقیت رزرو شد.");
    }

    public async Task<IReadOnlyList<CalendarEventDto>> GetCalendarEventsAsync(
        ActorContext actor,
        System.DateTime rangeStartUtc,
        System.DateTime rangeEndUtc,
        Guid? teacherIdFilter = null,
        Guid? studentIdFilter = null,
        Guid? roomIdFilter = null,
        Guid? instrumentIdFilter = null,
        CancellationToken ct = default)
    {
        var query = _db.ClassSessions
            .AsNoTracking()
            .Include(s => s.Student)
            .Include(s => s.Teacher)
            .Include(s => s.Instrument)
            .Include(s => s.Room)
            .Where(s => s.StartAtUtc < rangeEndUtc && s.EndAtUtc > rangeStartUtc);

        // Mandatory server-side scope isolation
        if (actor.IsAdmin)
        {
            if (teacherIdFilter.HasValue) query = query.Where(s => s.TeacherUserId == teacherIdFilter.Value);
            if (studentIdFilter.HasValue) query = query.Where(s => s.StudentUserId == studentIdFilter.Value);
        }
        else if (actor.IsTeacher)
        {
            var ownedIds = await _authScope.GetAccessibleStudentIdsForTeacherAsync(actor.UserId, ct);
            query = query.Where(s => s.TeacherUserId == actor.UserId && ownedIds.Contains(s.StudentUserId));
            if (studentIdFilter.HasValue) query = query.Where(s => s.StudentUserId == studentIdFilter.Value);
        }
        else
        {
            query = query.Where(s => s.StudentUserId == actor.UserId);
        }

        if (roomIdFilter.HasValue) query = query.Where(s => s.RoomId == roomIdFilter.Value);
        if (instrumentIdFilter.HasValue) query = query.Where(s => s.InstrumentId == instrumentIdFilter.Value);

        var list = await query.OrderBy(s => s.StartAtUtc).ToListAsync(ct);
        return list.Select(s => ToCalendarDto(s, s.Room.Name)).ToList();
    }

    private CalendarEventDto ToCalendarDto(ClassSession s, string roomName) =>
        new(
            s.Id,
            s.EnrollmentId,
            s.StudentUserId,
            s.Student.FullName,
            s.TeacherUserId,
            s.Teacher.FullName,
            s.InstrumentId,
            s.Instrument.Name,
            s.RoomId,
            roomName,
            s.SessionNumber,
            s.SessionType,
            s.Status,
            s.StartAtUtc,
            s.EndAtUtc,
            _jalali.FormatDate(s.StartAtUtc),
            _jalali.FormatTime(s.StartAtUtc),
            _jalali.FormatTime(s.EndAtUtc),
            s.DurationMinutes);
}
