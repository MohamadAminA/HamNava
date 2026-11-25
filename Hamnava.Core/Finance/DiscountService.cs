using Hamnava.Core.Common;
using Hamnava.Core.DateTime;
using Hamnava.Core.Services;
using Hamnava.DataLayer.Context;
using Hamnava.Domain.Constants;
using Hamnava.Domain.Entities.Finance;
using Hamnava.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Hamnava.Core.Finance;

public sealed record DiscountEvaluationResult(
    Guid DiscountCodeId,
    string Code,
    decimal OriginalAmountIrr,
    decimal DiscountAmountIrr,
    decimal FinalPayableAmountIrr);

public interface IDiscountService
{
    Task<ServiceResult<DiscountEvaluationResult>> ValidateAndCalculateAsync(
        string code,
        Guid studentUserId,
        decimal originalAmountIrr,
        Guid? coursePlanId = null,
        Guid? targetTeacherUserId = null,
        CancellationToken ct = default);

    Task<ServiceResult<DiscountCode?>> IssueBirthdayDiscountForUserAsync(
        Guid userId,
        int jalaliYear,
        CancellationToken ct = default);
}

public sealed class DiscountService : IDiscountService
{
    private readonly HamnavaDbContext _db;
    private readonly ISystemSettingsService _settings;
    private readonly IJalaliDateService _jalali;

    public DiscountService(
        HamnavaDbContext db,
        ISystemSettingsService settings,
        IJalaliDateService jalali)
    {
        _db = db;
        _settings = settings;
        _jalali = jalali;
    }

    public static decimal ComputeDiscountAmount(
        decimal originalAmountIrr,
        DiscountType discountType,
        decimal discountValue,
        decimal? maxDiscountAmountIrr)
    {
        if (originalAmountIrr <= 0m || discountValue <= 0m)
        {
            return 0m;
        }

        var raw = discountType switch
        {
            DiscountType.Percentage => decimal.Round(originalAmountIrr * (Math.Min(discountValue, 100m) / 100m), 0, MidpointRounding.AwayFromZero),
            DiscountType.FixedAmountIrr => decimal.Round(discountValue, 0, MidpointRounding.AwayFromZero),
            _ => 0m
        };

        if (maxDiscountAmountIrr.HasValue && raw > maxDiscountAmountIrr.Value)
        {
            raw = maxDiscountAmountIrr.Value;
        }

        return Math.Min(raw, originalAmountIrr);
    }

    public async Task<ServiceResult<DiscountEvaluationResult>> ValidateAndCalculateAsync(
        string code,
        Guid studentUserId,
        decimal originalAmountIrr,
        Guid? coursePlanId = null,
        Guid? targetTeacherUserId = null,
        CancellationToken ct = default)
    {
        var normalized = (code ?? string.Empty).Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return ServiceResult<DiscountEvaluationResult>.Fail("کد تخفیف وارد نشده است.", "EMPTY_CODE");
        }

        var discount = await _db.DiscountCodes
            .Include(d => d.EligiblePlans)
            .FirstOrDefaultAsync(d => d.Code == normalized, ct);

        if (discount is null || !discount.IsActive)
        {
            return ServiceResult<DiscountEvaluationResult>.Fail("کد تخفیف معتبر نیست یا غیرفعال شده است.", "INVALID_CODE");
        }

        var nowUtc = _jalali.UtcNow;
        if (discount.StartsAtUtc > nowUtc || (discount.ExpiresAtUtc.HasValue && discount.ExpiresAtUtc.Value < nowUtc))
        {
            return ServiceResult<DiscountEvaluationResult>.Fail("مهلت استفاده از این کد تخفیف به پایان رسیده است.", "EXPIRED_CODE");
        }

        if (discount.MaxTotalUsages.HasValue && discount.CurrentUsageCount >= discount.MaxTotalUsages.Value)
        {
            return ServiceResult<DiscountEvaluationResult>.Fail("ظرفیت استفاده از این کد تخفیف تکمیل شده است.", "EXHAUSTED_CODE");
        }

        var userUsageCount = await _db.DiscountUsages
            .AsNoTracking()
            .CountAsync(u => u.DiscountCodeId == discount.Id && u.UserId == studentUserId, ct);

        if (userUsageCount >= discount.MaxUsagesPerUser)
        {
            return ServiceResult<DiscountEvaluationResult>.Fail("شما قبلاً حداکثر دفعات مجاز از این کد تخفیف استفاده کرده‌اید.", "USER_LIMIT_REACHED");
        }

        if (discount.RestrictedToUserId.HasValue && discount.RestrictedToUserId.Value != studentUserId)
        {
            return ServiceResult<DiscountEvaluationResult>.Fail("این کد تخفیف اختصاصی کاربر دیگری است.", "USER_RESTRICTED");
        }

        // Strict Teacher-specific discount ownership check
        if (discount.Scope == DiscountScope.TeacherSpecific && discount.OwnerTeacherUserId.HasValue)
        {
            var teacherId = discount.OwnerTeacherUserId.Value;
            var isAssignedToTeacher = await _db.TeacherStudentAssignments
                .AsNoTracking()
                .AnyAsync(a => a.TeacherUserId == teacherId && a.StudentUserId == studentUserId && a.IsActive, ct);

            var matchesTargetTeacher = !targetTeacherUserId.HasValue || targetTeacherUserId.Value == teacherId;

            if (!isAssignedToTeacher || !matchesTargetTeacher)
            {
                return ServiceResult<DiscountEvaluationResult>.Fail("این کد تخفیف فقط برای هنرجویان مدرس صادرکننده قابل استفاده است.", "TEACHER_SCOPE_MISMATCH");
            }
        }

        if (discount.MinimumPayableAmountIrr.HasValue && originalAmountIrr < discount.MinimumPayableAmountIrr.Value)
        {
            return ServiceResult<DiscountEvaluationResult>.Fail(
                $"حداقل مبلغ خرید برای استفاده از این کد {CurrencyFormatter.FormatToman(discount.MinimumPayableAmountIrr.Value)} است.",
                "MIN_AMOUNT_NOT_MET");
        }

        if (coursePlanId.HasValue && discount.EligiblePlans.Count > 0 &&
            discount.EligiblePlans.All(p => p.CoursePlanId != coursePlanId.Value))
        {
            return ServiceResult<DiscountEvaluationResult>.Fail("این کد تخفیف برای دوره انتخاب‌شده قابل اعمال نیست.", "PLAN_NOT_ELIGIBLE");
        }

        var discountAmount = ComputeDiscountAmount(
            originalAmountIrr,
            discount.DiscountType,
            discount.DiscountValue,
            discount.MaxDiscountAmountIrr);

        var finalAmount = Math.Max(0m, originalAmountIrr - discountAmount);

        return ServiceResult<DiscountEvaluationResult>.Ok(
            new DiscountEvaluationResult(discount.Id, discount.Code, originalAmountIrr, discountAmount, finalAmount),
            "کد تخفیف با موفقیت اعمال شد.");
    }

    public async Task<ServiceResult<DiscountCode?>> IssueBirthdayDiscountForUserAsync(
        Guid userId,
        int jalaliYear,
        CancellationToken ct = default)
    {
        var enabled = await _settings.GetBoolAsync(SystemSettingKeys.BirthdayDiscountEnabled, true, ct);
        if (!enabled)
        {
            return ServiceResult<DiscountCode?>.Ok(null);
        }

        var cycleKey = $"BIRTHDAY:{userId}:{jalaliYear}";
        var existing = await _db.DiscountCodes.AsNoTracking().FirstOrDefaultAsync(d => d.RewardCycleKey == cycleKey, ct);
        if (existing is not null)
        {
            return ServiceResult<DiscountCode?>.Ok(existing, "تخفیف تولد این سال قبلاً صادر شده است.");
        }

        var typeRaw = await _settings.GetStringAsync(SystemSettingKeys.BirthdayDiscountType, "Percentage", ct);
        var discountType = Enum.TryParse<DiscountType>(typeRaw, true, out var parsed) ? parsed : DiscountType.Percentage;
        var value = await _settings.GetDecimalAsync(SystemSettingKeys.BirthdayDiscountValue, 15m, ct);
        var maxAmount = await _settings.GetDecimalAsync(SystemSettingKeys.BirthdayDiscountMaxAmountIrr, 5_000_000m, ct);
        var validityDays = await _settings.GetIntAsync(SystemSettingKeys.BirthdayDiscountValidityDays, 14, ct);

        var code = new DiscountCode
        {
            Code = $"HBD-{jalaliYear}-{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}",
            Title = $"هدیه تولد سال {jalaliYear}",
            Scope = DiscountScope.BirthdayReward,
            DiscountType = discountType,
            DiscountValue = value,
            MaxDiscountAmountIrr = maxAmount > 0m ? maxAmount : null,
            MaxTotalUsages = 1,
            MaxUsagesPerUser = 1,
            StartsAtUtc = _jalali.UtcNow,
            ExpiresAtUtc = _jalali.UtcNow.AddDays(validityDays),
            IsActive = true,
            RestrictedToUserId = userId,
            RewardCycleKey = cycleKey
        };

        _db.DiscountCodes.Add(code);
        await _db.SaveChangesAsync(ct);
        return ServiceResult<DiscountCode?>.Ok(code, "کد تخفیف تولد صادر شد.");
    }
}
