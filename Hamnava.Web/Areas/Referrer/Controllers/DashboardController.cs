using Hamnava.Core.DateTime;
using Hamnava.Core.Finance;
using Hamnava.DataLayer.Context;
using Hamnava.Domain.Enums;
using Hamnava.Web.Infrastructure;
using Hamnava.Web.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Hamnava.Web.Areas.Referrer.Controllers;

[Area("Referrer")]
[Authorize(Policy = "ReferrerOrAdmin")]
public sealed class DashboardController : Controller
{
    private readonly HamnavaDbContext _db;
    private readonly IFinancialService _financialService;
    private readonly IJalaliDateService _jalali;

    public DashboardController(
        HamnavaDbContext db,
        IFinancialService financialService,
        IJalaliDateService jalali)
    {
        _db = db;
        _financialService = financialService;
        _jalali = jalali;
    }

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var actor = HttpContext.ToActorContext();
        var referrer = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == actor.UserId, ct);
        if (referrer is null)
        {
            return Unauthorized();
        }

        var code = string.IsNullOrWhiteSpace(referrer.ReferralCode)
            ? $"HN-{referrer.Id.ToString("N")[..6].ToUpperInvariant()}"
            : referrer.ReferralCode;

        var referredStudents = await _db.Users
            .AsNoTracking()
            .Where(u => u.ReferredByUserId == actor.UserId)
            .OrderByDescending(u => u.CreatedAtUtc)
            .ToListAsync(ct);

        var rewards = await _db.ReferralRewards
            .AsNoTracking()
            .Where(r => r.ReferringUserId == actor.UserId)
            .OrderByDescending(r => r.CreatedAtUtc)
            .ToListAsync(ct);

        var rewardByStudent = rewards
            .GroupBy(r => r.InvitedStudentUserId)
            .ToDictionary(g => g.Key, g => g.First());

        var historyRows = referredStudents.Select(st =>
        {
            rewardByStudent.TryGetValue(st.Id, out var reward);
            var masked = MaskStudentName(st.FirstName, st.LastName);
            var statusFa = reward is null
                ? "در انتظار پرداخت اولین دوره"
                : reward.WalletLedgerId.HasValue
                    ? "واریز شده به کیف پول"
                    : reward.IsRedeemed
                        ? "استفاده شده"
                        : "صادر و آماده استفاده";

            var rewardSummary = reward is null
                ? "—"
                : reward.RewardType == ReferralRewardType.DiscountPercentage
                    ? $"{reward.RewardValue:0}% تخفیف"
                    : CurrencyFormatter.FormatToman(reward.RewardValue);

            return new ReferralHistoryRowViewModel(
                masked,
                _jalali.FormatDate(st.CreatedAtUtc),
                reward is not null,
                statusFa,
                rewardSummary);
        }).ToList();

        var wallet = await _db.Wallets.AsNoTracking().FirstOrDefaultAsync(w => w.UserId == actor.UserId, ct);
        var ledger = wallet is null
            ? []
            : await _db.WalletLedgers.AsNoTracking()
                .Where(l => l.WalletId == wallet.Id)
                .OrderByDescending(l => l.CreatedAtUtc)
                .Take(12)
                .ToListAsync(ct);

        var baseUrl = $"{Request.Scheme}://{Request.Host}";
        var vm = new ReferrerDashboardViewModel
        {
            ReferralCode = code,
            ReferralLink = $"{baseUrl}/account/register?ref={code}",
            TotalInvitedUsers = referredStudents.Count,
            RegisteredUsersCount = referredStudents.Count,
            PaidFirstCourseCount = rewards.Count,
            EligibleRewardsCount = rewards.Count(r => r.WalletLedgerId.HasValue || r.GeneratedDiscountCodeId.HasValue),
            AvailableBalanceIrr = wallet?.AvailableBalanceIrr ?? 0m,
            TotalEarnedIrr = wallet?.TotalEarnedIrr ?? 0m,
            TotalWithdrawnIrr = wallet?.TotalWithdrawnIrr ?? 0m,
            Referrals = historyRows,
            RecentLedgerEntries = ledger
        };

        ViewData["Title"] = "داشبورد معرف | آکادمی موسیقی هم‌نوا";
        return View(vm);
    }

    [HttpPost]
    public async Task<IActionResult> RequestWithdrawal(decimal amountIrr, string destinationSheba, CancellationToken ct)
    {
        var actor = HttpContext.ToActorContext();
        var result = await _financialService.RequestWithdrawalAsync(amountIrr, destinationSheba, actor, ct);
        TempData[result.Succeeded ? "SuccessMessage" : "ErrorMessage"] = string.IsNullOrWhiteSpace(result.Message) ? "درخواست برداشت با موفقیت ثبت شد." : result.Message;
        return RedirectToAction(nameof(Index));
    }

    private static string MaskStudentName(string firstName, string lastName)
    {
        var safeFirst = string.IsNullOrWhiteSpace(firstName) ? "هنرجو" : firstName.Trim();
        var safeLastInitial = string.IsNullOrWhiteSpace(lastName) ? string.Empty : $"{lastName.Trim()[0]}***";
        return $"{safeFirst} {safeLastInitial}".Trim();
    }
}
