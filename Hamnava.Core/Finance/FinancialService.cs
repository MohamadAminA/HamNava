using System.Data;
using Hamnava.Core.Common;
using Hamnava.Core.DateTime;
using Hamnava.Core.Security;
using Hamnava.Core.Services;
using Hamnava.DataLayer.Context;
using Hamnava.Domain.Entities.Education;
using Hamnava.Domain.Entities.Finance;
using Hamnava.Domain.Enums;
using Hamnava.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace Hamnava.Core.Finance;

public sealed record SessionCommissionBreakdown(
    decimal EffectiveSessionBaseIrr,
    decimal TeacherShareIrr,
    decimal ReferrerShareIrr,
    decimal AcademyNetShareIrr,
    decimal TeacherPercentageUsed);

/// <summary>
/// Pure deterministic financial math engine (isolated for unit tests).
/// All monetary math operates strictly in IRR using decimal arithmetic.
/// </summary>
public static class CommissionCalculator
{
    public static SessionCommissionBreakdown CalculateSessionShares(
        decimal originalPriceIrr,
        decimal finalPaidAmountIrr,
        int totalEligibleSessions,
        decimal teacherCommissionPercentage,
        CommissionCalculationBasis basis,
        FinancialRoundingMode roundingMode,
        ReferrerCommissionType? referrerCommissionType = null,
        decimal? referrerCommissionValue = null,
        bool deductReferrerFromAcademyShare = true)
    {
        if (totalEligibleSessions <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(totalEligibleSessions), "تعداد جلسات باید بزرگ‌تر از صفر باشد.");
        }

        var calculationTotalIrr = basis == CommissionCalculationBasis.PostDiscountFinalAmount
            ? finalPaidAmountIrr
            : originalPriceIrr;

        if (calculationTotalIrr < 0m)
        {
            calculationTotalIrr = 0m;
        }

        var perSessionRaw = calculationTotalIrr / totalEligibleSessions;
        var sessionBase = MoneyIrr.Round(perSessionRaw, roundingMode);
        var teacherShare = sessionBase.ApplyPercentage(teacherCommissionPercentage, roundingMode);

        var referrerShare = MoneyIrr.Zero;
        if (referrerCommissionType.HasValue && referrerCommissionValue.HasValue && referrerCommissionValue.Value > 0m)
        {
            referrerShare = referrerCommissionType.Value switch
            {
                ReferrerCommissionType.Percentage =>
                    sessionBase.ApplyPercentage(referrerCommissionValue.Value, roundingMode),
                ReferrerCommissionType.FixedAmountIrr =>
                    MoneyIrr.Round(referrerCommissionValue.Value / totalEligibleSessions, roundingMode),
                _ => MoneyIrr.Zero
            };
        }

        var academyShareIrr = sessionBase.AmountIrr - teacherShare.AmountIrr - (deductReferrerFromAcademyShare ? referrerShare.AmountIrr : 0m);
        if (academyShareIrr < 0m)
        {
            academyShareIrr = 0m;
        }

        return new SessionCommissionBreakdown(
            sessionBase.AmountIrr,
            teacherShare.AmountIrr,
            referrerShare.AmountIrr,
            academyShareIrr,
            teacherCommissionPercentage);
    }
}

public interface IFinancialService
{
    Task<ServiceResult<SessionCommissionBreakdown>> SettleSessionCommissionAsync(
        Guid classSessionId,
        ActorContext actor,
        CancellationToken ct = default);
    Task<ServiceResult> ReverseSessionSettlementAsync(
        Guid classSessionId,
        string reason,
        ActorContext actor,
        CancellationToken ct = default);
    Task<ServiceResult<ReferralReward?>> GrantFirstCourseReferralRewardIfEligibleAsync(
        Guid paymentId,
        CancellationToken ct = default);
    Task<ServiceResult<WalletWithdrawal>> RequestWithdrawalAsync(
        decimal amountIrr,
        string destinationSheba,
        ActorContext actor,
        CancellationToken ct = default);
    Task<ServiceResult<WalletWithdrawal>> ConfirmWithdrawalPaymentAsync(
        Guid withdrawalId,
        string bankReferenceNumber,
        string? notes,
        ActorContext adminActor,
        CancellationToken ct = default);
}

public sealed class FinancialService : IFinancialService
{
    private readonly HamnavaDbContext _db;
    private readonly ISystemSettingsService _settings;
    private readonly IAuthorizationScopeService _authScope;
    private readonly IJalaliDateService _jalali;

    public FinancialService(
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

    public async Task<ServiceResult<SessionCommissionBreakdown>> SettleSessionCommissionAsync(
        Guid classSessionId,
        ActorContext actor,
        CancellationToken ct = default)
    {
        var session = await _db.ClassSessions
            .Include(s => s.Enrollment)
            .Include(s => s.Student)
            .FirstOrDefaultAsync(s => s.Id == classSessionId, ct);

        if (session is null)
        {
            return ServiceResult<SessionCommissionBreakdown>.Fail("جلسه یافت نشد.", "SESSION_NOT_FOUND");
        }

        // Strict idempotency guard: never credit a session twice
        if (session.IsFinanciallySettled)
        {
            return ServiceResult<SessionCommissionBreakdown>.Ok(
                new SessionCommissionBreakdown(0, 0, 0, 0, session.Enrollment.TeacherCommissionPercentageSnapshot),
                "تسویه مالی این جلسه قبلاً انجام شده است.");
        }

        var financialSettings = await _settings.GetCurrentFinancialSettingAsync(ct);

        var eligibleForSettlement = session.Status switch
        {
            ClassSessionStatus.Present => true,
            ClassSessionStatus.UnauthorizedAbsence => financialSettings.SettleCommissionOnUnauthorizedAbsence,
            ClassSessionStatus.AuthorizedAbsence => financialSettings.SettleCommissionOnAuthorizedAbsence,
            _ => false
        };

        if (!eligibleForSettlement)
        {
            return ServiceResult<SessionCommissionBreakdown>.Ok(
                new SessionCommissionBreakdown(0, 0, 0, 0, session.Enrollment.TeacherCommissionPercentageSnapshot),
                "این وضعیت حضور مشمول تسویه پورسانت نیست.");
        }

        var enrollment = session.Enrollment;
        var hasReferrer = session.Student.ReferredByUserId.HasValue;

        var breakdown = CommissionCalculator.CalculateSessionShares(
            enrollment.OriginalPriceIrr,
            enrollment.FinalPaidAmountIrr,
            enrollment.TotalSessionsSnapshot,
            enrollment.TeacherCommissionPercentageSnapshot,
            enrollment.CommissionBasisSnapshot,
            enrollment.RoundingModeSnapshot,
            hasReferrer ? enrollment.ReferrerCommissionTypeSnapshot : null,
            hasReferrer ? enrollment.ReferrerCommissionValueSnapshot : null,
            financialSettings.DeductReferrerShareFromAcademyShare);

        // 1. Credit Teacher Wallet via immutable ledger with unique IdempotencyKey
        if (breakdown.TeacherShareIrr > 0m)
        {
            var teacherIdemKey = $"SESSION:{session.Id}:TEACHER:{session.TeacherUserId}";
            await PostImmutableLedgerEntryAsync(
                session.TeacherUserId,
                WalletLedgerEntryType.SessionCredit,
                breakdown.TeacherShareIrr,
                teacherIdemKey,
                $"سهم مدرس از جلسه شماره {session.SessionNumber} ({session.Student.FullName})",
                session.Id,
                enrollment.PaymentId,
                session.StudentUserId,
                breakdown.TeacherPercentageUsed,
                breakdown.EffectiveSessionBaseIrr,
                actor.UserId,
                ct);
        }

        // 2. Credit Referrer Wallet if applicable
        if (hasReferrer && breakdown.ReferrerShareIrr > 0m)
        {
            var referrerId = session.Student.ReferredByUserId!.Value;
            var referrerIdemKey = $"SESSION:{session.Id}:REFERRER:{referrerId}";
            await PostImmutableLedgerEntryAsync(
                referrerId,
                WalletLedgerEntryType.ReferralCredit,
                breakdown.ReferrerShareIrr,
                referrerIdemKey,
                $"پورسانت معرفی از جلسه شماره {session.SessionNumber}",
                session.Id,
                enrollment.PaymentId,
                session.StudentUserId,
                enrollment.ReferrerCommissionTypeSnapshot == ReferrerCommissionType.Percentage
                    ? enrollment.ReferrerCommissionValueSnapshot
                    : null,
                breakdown.EffectiveSessionBaseIrr,
                actor.UserId,
                ct);
        }

        session.IsFinanciallySettled = true;
        session.FinanciallySettledAtUtc = _jalali.UtcNow;
        await _db.SaveChangesAsync(ct);

        return ServiceResult<SessionCommissionBreakdown>.Ok(breakdown, "تسویه مالی جلسه با موفقیت انجام شد.");
    }

    public async Task<ServiceResult> ReverseSessionSettlementAsync(
        Guid classSessionId,
        string reason,
        ActorContext actor,
        CancellationToken ct = default)
    {
        var session = await _db.ClassSessions.FirstOrDefaultAsync(s => s.Id == classSessionId, ct);
        if (session is null || !session.IsFinanciallySettled)
        {
            return ServiceResult.Ok("تسویه مالی فعالی برای برگشت وجود ندارد.");
        }

        // Never mutate historical ledger rows — post compensating Reversal entries
        var originalCredits = await _db.WalletLedgers
            .Where(l => l.RelatedSessionId == classSessionId && l.AmountIrr > 0m &&
                        (l.EntryType == WalletLedgerEntryType.SessionCredit || l.EntryType == WalletLedgerEntryType.ReferralCredit))
            .ToListAsync(ct);

        foreach (var credit in originalCredits)
        {
            var reversalKey = $"REVERSAL:{credit.Id}";
            if (await _db.WalletLedgers.AnyAsync(l => l.IdempotencyKey == reversalKey, ct))
            {
                continue;
            }

            await PostImmutableLedgerEntryAsync(
                credit.BeneficiaryUserId,
                WalletLedgerEntryType.Reversal,
                -credit.AmountIrr,
                reversalKey,
                $"برگشت سند مالی جلسه به دلیل اصلاح حضور و غیاب: {reason}",
                classSessionId,
                credit.RelatedPaymentId,
                credit.RelatedStudentUserId,
                credit.AppliedCommissionPercentageSnapshot,
                credit.CalculationBaseAmountIrrSnapshot,
                actor.UserId,
                ct,
                reversedLedgerId: credit.Id,
                allowTemporaryNegativeForReversal: true);
        }

        session.IsFinanciallySettled = false;
        session.FinanciallySettledAtUtc = null;
        await _db.SaveChangesAsync(ct);

        return ServiceResult.Ok("اسناد مالی اصلاحی (برگشتی) با موفقیت ثبت شد.");
    }

    public async Task<ServiceResult<ReferralReward?>> GrantFirstCourseReferralRewardIfEligibleAsync(
        Guid paymentId,
        CancellationToken ct = default)
    {
        var payment = await _db.Payments
            .Include(p => p.User)
            .FirstOrDefaultAsync(p => p.Id == paymentId && p.Status == PaymentStatus.Verified, ct);

        if (payment is null || payment.PaymentType != PaymentType.Tuition)
        {
            return ServiceResult<ReferralReward?>.Ok(null);
        }

        var student = payment.User;
        if (!student.ReferredByUserId.HasValue || student.ReferredByUserId.Value == student.Id)
        {
            return ServiceResult<ReferralReward?>.Ok(null);
        }

        // Ensure reward is granted EXACTLY ONCE per invited student
        var existingReward = await _db.ReferralRewards
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.InvitedStudentUserId == student.Id, ct);

        if (existingReward is not null)
        {
            return ServiceResult<ReferralReward?>.Ok(existingReward, "پاداش معرفی این هنرجو قبلاً ثبت شده است.");
        }

        var financialSettings = await _settings.GetCurrentFinancialSettingAsync(ct);
        var referrerId = student.ReferredByUserId.Value;

        DiscountCode? generatedDiscount = null;
        if (financialSettings.StudentReferralRewardType is ReferralRewardType.DiscountPercentage or ReferralRewardType.DiscountFixedIrr)
        {
            var code = $"REF-{Guid.NewGuid().ToString("N")[..8].ToUpperInvariant()}";
            generatedDiscount = new DiscountCode
            {
                Code = code,
                Title = $"پاداش معرفی هنرجو ({student.FullName})",
                Scope = DiscountScope.ReferralReward,
                DiscountType = financialSettings.StudentReferralRewardType == ReferralRewardType.DiscountPercentage
                    ? DiscountType.Percentage
                    : DiscountType.FixedAmountIrr,
                DiscountValue = financialSettings.StudentReferralRewardValue,
                MaxTotalUsages = 1,
                MaxUsagesPerUser = 1,
                StartsAtUtc = _jalali.UtcNow,
                ExpiresAtUtc = _jalali.UtcNow.AddDays(financialSettings.StudentReferralRewardValidityDays),
                IsActive = true,
                RestrictedToUserId = referrerId
            };
            _db.DiscountCodes.Add(generatedDiscount);
        }

        var reward = new ReferralReward
        {
            ReferringUserId = referrerId,
            InvitedStudentUserId = student.Id,
            FirstSuccessfulPaymentId = payment.Id,
            RewardType = financialSettings.StudentReferralRewardType,
            RewardValue = financialSettings.StudentReferralRewardValue,
            GeneratedDiscountCode = generatedDiscount,
            ExpiresAtUtc = _jalali.UtcNow.AddDays(financialSettings.StudentReferralRewardValidityDays)
        };

        _db.ReferralRewards.Add(reward);
        await _db.SaveChangesAsync(ct);

        return ServiceResult<ReferralReward?>.Ok(reward, "پاداش معرفی با موفقیت صادر شد.");
    }

    public async Task<ServiceResult<WalletWithdrawal>> RequestWithdrawalAsync(
        decimal amountIrr,
        string destinationSheba,
        ActorContext actor,
        CancellationToken ct = default)
    {
        var financialSettings = await _settings.GetCurrentFinancialSettingAsync(ct);
        if (amountIrr < financialSettings.MinimumWithdrawalAmountIrr)
        {
            return ServiceResult<WalletWithdrawal>.Fail(
                $"حداقل مبلغ درخواست برداشت {CurrencyFormatter.FormatToman(financialSettings.MinimumWithdrawalAmountIrr)} است.",
                "BELOW_MIN_WITHDRAWAL");
        }

        var wallet = await GetOrCreateWalletAsync(actor.UserId, ct);
        if (wallet.AvailableBalanceIrr < amountIrr)
        {
            return ServiceResult<WalletWithdrawal>.Fail("موجودی قابل برداشت کیف پول کافی نیست.", "INSUFFICIENT_BALANCE");
        }

        var withdrawal = new WalletWithdrawal
        {
            WalletId = wallet.Id,
            BeneficiaryUserId = actor.UserId,
            RequestedAmountIrr = decimal.Round(amountIrr, 0, MidpointRounding.AwayFromZero),
            DestinationSheba = destinationSheba.Trim(),
            Status = WithdrawalStatus.Requested
        };

        _db.WalletWithdrawals.Add(withdrawal);
        await _db.SaveChangesAsync(ct);
        return ServiceResult<WalletWithdrawal>.Ok(withdrawal, "درخواست برداشت ثبت شد و در انتظار بررسی مدیریت است.");
    }

    public async Task<ServiceResult<WalletWithdrawal>> ConfirmWithdrawalPaymentAsync(
        Guid withdrawalId,
        string bankReferenceNumber,
        string? notes,
        ActorContext adminActor,
        CancellationToken ct = default)
    {
        if (!adminActor.IsAdmin)
        {
            return ServiceResult<WalletWithdrawal>.Fail("فقط مدیر سیستم مجاز به تایید تسویه است.", "FORBIDDEN");
        }

        await using var tx = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);

        var withdrawal = await _db.WalletWithdrawals
            .Include(w => w.Wallet)
            .FirstOrDefaultAsync(w => w.Id == withdrawalId, ct);

        if (withdrawal is null)
        {
            return ServiceResult<WalletWithdrawal>.Fail("درخواست برداشت یافت نشد.", "NOT_FOUND");
        }

        if (withdrawal.Status == WithdrawalStatus.Paid)
        {
            return ServiceResult<WalletWithdrawal>.Ok(withdrawal, "این درخواست قبلاً تسویه شده است.");
        }

        if (withdrawal.Wallet.AvailableBalanceIrr < withdrawal.RequestedAmountIrr)
        {
            return ServiceResult<WalletWithdrawal>.Fail("موجودی فعلی کیف پول برای پرداخت کافی نیست.", "INSUFFICIENT_BALANCE");
        }

        var idemKey = $"WITHDRAWAL:{withdrawal.Id}";
        await PostImmutableLedgerEntryAsync(
            withdrawal.BeneficiaryUserId,
            WalletLedgerEntryType.SettlementWithdrawal,
            -withdrawal.RequestedAmountIrr,
            idemKey,
            $"تسویه و واریز برداشت به شماره پیگیری {bankReferenceNumber}",
            null,
            null,
            null,
            null,
            null,
            adminActor.UserId,
            ct,
            relatedWithdrawalId: withdrawal.Id);

        withdrawal.Status = WithdrawalStatus.Paid;
        withdrawal.ReviewedByUserId = adminActor.UserId;
        withdrawal.ReviewedAtUtc = _jalali.UtcNow;
        withdrawal.PaidAtUtc = _jalali.UtcNow;
        withdrawal.BankReferenceNumber = bankReferenceNumber.Trim();
        withdrawal.ReviewNotes = notes?.Trim();

        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        await _authScope.WriteAuditLogAsync(adminActor, "ConfirmWithdrawal", nameof(WalletWithdrawal), withdrawal.Id.ToString(), null, withdrawal, ct);
        return ServiceResult<WalletWithdrawal>.Ok(withdrawal, "تسویه با موفقیت ثبت و از کیف پول کسر شد.");
    }

    private async Task PostImmutableLedgerEntryAsync(
        Guid beneficiaryUserId,
        WalletLedgerEntryType entryType,
        decimal signedAmountIrr,
        string idempotencyKey,
        string description,
        Guid? relatedSessionId,
        Guid? relatedPaymentId,
        Guid? relatedStudentUserId,
        decimal? appliedPercentageSnapshot,
        decimal? baseAmountSnapshot,
        Guid? createdByUserId,
        CancellationToken ct,
        Guid? relatedWithdrawalId = null,
        Guid? reversedLedgerId = null,
        bool allowTemporaryNegativeForReversal = false)
    {
        if (await _db.WalletLedgers.AnyAsync(l => l.IdempotencyKey == idempotencyKey, ct))
        {
            return;
        }

        var wallet = await GetOrCreateWalletAsync(beneficiaryUserId, ct);
        var newBalance = wallet.AvailableBalanceIrr + signedAmountIrr;

        if (!allowTemporaryNegativeForReversal && newBalance < 0m)
        {
            var policy = await _settings.GetCurrentFinancialSettingAsync(ct);
            if (!policy.AllowNegativeWalletBalance)
            {
                throw new InvalidOperationException("موجودی کیف پول نمی‌تواند منفی شود.");
            }
        }

        wallet.AvailableBalanceIrr = newBalance;
        if (signedAmountIrr > 0m)
        {
            wallet.TotalEarnedIrr += signedAmountIrr;
        }
        else if (entryType == WalletLedgerEntryType.SettlementWithdrawal)
        {
            wallet.TotalWithdrawnIrr += Math.Abs(signedAmountIrr);
        }

        _db.WalletLedgers.Add(new WalletLedger
        {
            WalletId = wallet.Id,
            BeneficiaryUserId = beneficiaryUserId,
            EntryType = entryType,
            AmountIrr = signedAmountIrr,
            BalanceAfterIrr = newBalance,
            RelatedSessionId = relatedSessionId,
            RelatedPaymentId = relatedPaymentId,
            RelatedStudentUserId = relatedStudentUserId,
            RelatedWithdrawalId = relatedWithdrawalId,
            ReversedLedgerId = reversedLedgerId,
            AppliedCommissionPercentageSnapshot = appliedPercentageSnapshot,
            CalculationBaseAmountIrrSnapshot = baseAmountSnapshot,
            Description = description,
            IdempotencyKey = idempotencyKey,
            CreatedByUserId = createdByUserId
        });
    }

    private async Task<Wallet> GetOrCreateWalletAsync(Guid userId, CancellationToken ct)
    {
        var wallet = await _db.Wallets.FirstOrDefaultAsync(w => w.UserId == userId, ct);
        if (wallet is not null)
        {
            return wallet;
        }

        wallet = new Wallet { UserId = userId };
        _db.Wallets.Add(wallet);
        await _db.SaveChangesAsync(ct);
        return wallet;
    }
}
