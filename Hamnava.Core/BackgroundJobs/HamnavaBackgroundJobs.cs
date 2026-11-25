using Hamnava.Core.DateTime;
using Hamnava.Core.Finance;
using Hamnava.Core.Messaging;
using Hamnava.Core.Services;
using Hamnava.DataLayer.Context;
using Hamnava.Domain.Constants;
using Hamnava.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Hamnava.Core.BackgroundJobs;

public interface IHamnavaRecurringJobs
{
    Task DispatchUpcomingLessonRemindersAsync(CancellationToken ct = default);
    Task ProcessDailyBirthdayDiscountsAsync(CancellationToken ct = default);
    Task DispatchRentalExpirationRemindersAsync(CancellationToken ct = default);
    Task CleanupExpiredOtpsAndPendingPaymentsAsync(CancellationToken ct = default);
    Task ProcessSmsCampaignAsync(Guid campaignId, CancellationToken ct = default);
}

/// <summary>
/// Idempotent Hangfire background jobs persisted in SQL Server.
/// Safe against retries: every notification and discount carries a unique idempotency key.
/// </summary>
public sealed class HamnavaRecurringJobs : IHamnavaRecurringJobs
{
    private readonly HamnavaDbContext _db;
    private readonly ISystemSettingsService _settings;
    private readonly IDiscountService _discountService;
    private readonly ISmsService _smsService;
    private readonly IJalaliDateService _jalali;
    private readonly ILogger<HamnavaRecurringJobs> _logger;

    public HamnavaRecurringJobs(
        HamnavaDbContext db,
        ISystemSettingsService settings,
        IDiscountService discountService,
        ISmsService smsService,
        IJalaliDateService jalali,
        ILogger<HamnavaRecurringJobs> logger)
    {
        _db = db;
        _settings = settings;
        _discountService = discountService;
        _smsService = smsService;
        _jalali = jalali;
        _logger = logger;
    }

    public async Task DispatchUpcomingLessonRemindersAsync(CancellationToken ct = default)
    {
        var intervals = await _settings.GetIntListAsync(SystemSettingKeys.LessonReminderIntervalsHours, [24, 2], ct);
        var nowUtc = _jalali.UtcNow;

        foreach (var hours in intervals)
        {
            var windowStart = nowUtc.AddHours(hours).AddMinutes(-15);
            var windowEnd = nowUtc.AddHours(hours).AddMinutes(15);

            var sessions = await _db.ClassSessions
                .AsNoTracking()
                .Include(s => s.Student)
                .Include(s => s.Teacher)
                .Include(s => s.Instrument)
                .Include(s => s.Room)
                .Where(s => s.Status == ClassSessionStatus.Scheduled && s.StartAtUtc >= windowStart && s.StartAtUtc <= windowEnd)
                .ToListAsync(ct);

            foreach (var session in sessions)
            {
                // Include StartAtUtc ticks in idempotency key so rescheduled lessons receive fresh reminders
                var idemKey = $"REMINDER:{session.Id}:{session.StartAtUtc.Ticks}:{hours}H";
                var vars = new Dictionary<string, string>
                {
                    ["StudentName"] = session.Student.FirstName,
                    ["InstrumentName"] = session.Instrument.Name,
                    ["TeacherName"] = session.Teacher.FullName,
                    ["LessonDate"] = _jalali.FormatDate(session.StartAtUtc),
                    ["LessonTime"] = _jalali.FormatTime(session.StartAtUtc),
                    ["RoomName"] = session.Room.Name
                };

                await _smsService.SendTemplateSmsAsync(
                    SmsTriggerType.LessonReminder,
                    session.Student.MobileNumber,
                    vars,
                    session.StudentUserId,
                    "LessonReminder",
                    session.Id,
                    idemKey,
                    null,
                    ct);
            }
        }
    }

    public async Task ProcessDailyBirthdayDiscountsAsync(CancellationToken ct = default)
    {
        var today = _jalali.TehranToday;
        var (jalaliYear, _, _) = _jalali.ToJalaliParts(today);

        var usersWithBirthDate = await _db.Users
            .AsNoTracking()
            .Where(u => u.Status == UserStatus.Active && u.BirthDateGregorian.HasValue)
            .ToListAsync(ct);

        foreach (var user in usersWithBirthDate)
        {
            if (!_jalali.IsBirthdayMatch(user.BirthDateGregorian!.Value, today))
            {
                continue;
            }

            var issued = await _discountService.IssueBirthdayDiscountForUserAsync(user.Id, jalaliYear, ct);
            if (!issued.Succeeded || issued.Data is null)
            {
                continue;
            }

            var smsIdemKey = $"BIRTHDAY_SMS:{user.Id}:{jalaliYear}";
            var vars = new Dictionary<string, string>
            {
                ["StudentName"] = user.FirstName,
                ["DiscountCode"] = issued.Data.Code,
                ["ExpiryDate"] = issued.Data.ExpiresAtUtc.HasValue ? _jalali.FormatDate(issued.Data.ExpiresAtUtc.Value) : "-"
            };

            await _smsService.SendTemplateSmsAsync(
                SmsTriggerType.BirthdayGreeting,
                user.MobileNumber,
                vars,
                user.Id,
                "BirthdayGreeting",
                issued.Data.Id,
                smsIdemKey,
                null,
                ct);
        }
    }

    public async Task DispatchRentalExpirationRemindersAsync(CancellationToken ct = default)
    {
        var reminderDays = await _settings.GetIntListAsync(SystemSettingKeys.RentalExpirationReminderDays, [3, 1], ct);
        var today = _jalali.TehranToday;

        foreach (var daysLeft in reminderDays)
        {
            var targetEndDate = today.AddDays(daysLeft);
            var expiringRentals = await _db.InstrumentRentals
                .AsNoTracking()
                .Include(r => r.Student)
                .Include(r => r.Instrument)
                .Where(r => (r.Status == RentalStatus.Active || r.Status == RentalStatus.Extended) && r.EndDateGregorian == targetEndDate)
                .ToListAsync(ct);

            foreach (var rental in expiringRentals)
            {
                var idemKey = $"RENTAL_EXPIRY:{rental.Id}:{targetEndDate:yyyyMMdd}:{daysLeft}D";
                var vars = new Dictionary<string, string>
                {
                    ["StudentName"] = rental.Student.FirstName,
                    ["InstrumentName"] = rental.Instrument.Name,
                    ["EndDate"] = _jalali.FormatDate(rental.EndDateGregorian),
                    ["RemainingDays"] = _jalali.ToPersianDigits(daysLeft.ToString())
                };

                await _smsService.SendTemplateSmsAsync(
                    SmsTriggerType.RentalExpirationReminder,
                    rental.Student.MobileNumber,
                    vars,
                    rental.StudentUserId,
                    "RentalExpiry",
                    rental.Id,
                    idemKey,
                    null,
                    ct);
            }
        }
    }

    public async Task CleanupExpiredOtpsAndPendingPaymentsAsync(CancellationToken ct = default)
    {
        var nowUtc = _jalali.UtcNow;
        var otpCutoff = nowUtc.AddDays(-7);

        var staleOtps = await _db.OtpRequests
            .Where(o => o.ExpiresAtUtc < otpCutoff)
            .ExecuteDeleteAsync(ct);

        var expiredPayments = await _db.Payments
            .Where(p => (p.Status == PaymentStatus.Pending || p.Status == PaymentStatus.Redirected) && p.ExpiresAtUtc < nowUtc)
            .ToListAsync(ct);

        foreach (var payment in expiredPayments)
        {
            payment.Status = PaymentStatus.Expired;
        }

        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Maintenance cleanup completed: Deleted {OtpCount} old OTPs, expired {PaymentCount} stale payments",
            staleOtps, expiredPayments.Count);
    }

    public async Task ProcessSmsCampaignAsync(Guid campaignId, CancellationToken ct = default)
    {
        var campaign = await _db.SmsCampaigns
            .Include(c => c.Recipients)
            .FirstOrDefaultAsync(c => c.Id == campaignId, ct);

        if (campaign is null || campaign.Status == SmsCampaignStatus.Completed)
        {
            return;
        }

        campaign.Status = SmsCampaignStatus.Processing;
        await _db.SaveChangesAsync(ct);

        foreach (var recipient in campaign.Recipients.Where(r => r.DeliveryStatus == SmsDeliveryStatus.Queued))
        {
            var res = await _smsService.SendDirectManualSmsAsync(
                recipient.MobileNumber,
                campaign.MessageBody,
                recipient.UserId,
                campaign.CreatedByTeacherOrAdminId,
                ct);

            if (res.Succeeded)
            {
                recipient.DeliveryStatus = SmsDeliveryStatus.AcceptedByProvider;
                campaign.SubmittedCount++;
            }
            else
            {
                recipient.DeliveryStatus = SmsDeliveryStatus.Failed;
                recipient.ErrorMessage = res.Message;
                campaign.FailedCount++;
            }

            // Gentle throttling to avoid starving OTP delivery or tripping provider rate limits
            await Task.Delay(150, ct);
        }

        campaign.Status = campaign.FailedCount == 0
            ? SmsCampaignStatus.Completed
            : campaign.SubmittedCount > 0 ? SmsCampaignStatus.PartiallyFailed : SmsCampaignStatus.Failed;

        await _db.SaveChangesAsync(ct);
    }
}
