using FluentAssertions;
using Hamnava.Core.Finance;
using Hamnava.Core.Services;
using Hamnava.Domain.Entities.Instruments;
using Hamnava.Domain.Enums;
using Xunit;

namespace Hamnava.Tests.Unit;

public sealed class FinancialAndCommissionTests
{
    [Fact]
    public void SessionCommission_PostDiscountBasis_CalculatesExactTeacherAndAcademySharesInIrr()
    {
        // Course original = 40,000,000 IRR (4M Toman), after 10% discount = 36,000,000 IRR, 4 sessions -> 9,000,000 IRR/session
        var result = CommissionCalculator.CalculateSessionShares(
            originalPriceIrr: 40_000_000m,
            finalPaidAmountIrr: 36_000_000m,
            totalEligibleSessions: 4,
            teacherCommissionPercentage: 55m,
            basis: CommissionCalculationBasis.PostDiscountFinalAmount,
            roundingMode: FinancialRoundingMode.AwayFromZeroToNearest1000Irr);

        result.EffectiveSessionBaseIrr.Should().Be(9_000_000m);
        result.TeacherShareIrr.Should().Be(4_950_000m);
        result.ReferrerShareIrr.Should().Be(0m);
        result.AcademyNetShareIrr.Should().Be(4_050_000m);
    }

    [Fact]
    public void SessionCommission_PreDiscountBasis_UsesOriginalPriceEvenWhenDiscountApplied()
    {
        var result = CommissionCalculator.CalculateSessionShares(
            originalPriceIrr: 40_000_000m,
            finalPaidAmountIrr: 32_000_000m,
            totalEligibleSessions: 4,
            teacherCommissionPercentage: 50m,
            basis: CommissionCalculationBasis.PreDiscountOriginalAmount,
            roundingMode: FinancialRoundingMode.AwayFromZeroToNearest1000Irr);

        result.EffectiveSessionBaseIrr.Should().Be(10_000_000m);
        result.TeacherShareIrr.Should().Be(5_000_000m);
        result.AcademyNetShareIrr.Should().Be(5_000_000m);
    }

    [Fact]
    public void SessionCommission_WithReferrerShareDeductedFromAcademy_MaintainsExactConservation()
    {
        var result = CommissionCalculator.CalculateSessionShares(
            originalPriceIrr: 20_000_000m,
            finalPaidAmountIrr: 20_000_000m,
            totalEligibleSessions: 4,
            teacherCommissionPercentage: 50m,
            basis: CommissionCalculationBasis.PostDiscountFinalAmount,
            roundingMode: FinancialRoundingMode.AwayFromZeroToNearest1000Irr,
            referrerCommissionType: ReferrerCommissionType.Percentage,
            referrerCommissionValue: 10m,
            deductReferrerFromAcademyShare: true);

        result.EffectiveSessionBaseIrr.Should().Be(5_000_000m);
        result.TeacherShareIrr.Should().Be(2_500_000m);
        result.ReferrerShareIrr.Should().Be(500_000m);
        result.AcademyNetShareIrr.Should().Be(2_000_000m);
        (result.TeacherShareIrr + result.ReferrerShareIrr + result.AcademyNetShareIrr)
            .Should().Be(result.EffectiveSessionBaseIrr);
    }

    [Fact]
    public void DiscountComputation_PercentageWithMaxCap_EnforcesCapAndNeverExceedsOriginal()
    {
        var discount = DiscountService.ComputeDiscountAmount(
            originalAmountIrr: 50_000_000m,
            discountType: DiscountType.Percentage,
            discountValue: 30m, // Raw = 15,000,000
            maxDiscountAmountIrr: 10_000_000m);

        discount.Should().Be(10_000_000m);

        var fixedOverflow = DiscountService.ComputeDiscountAmount(
            originalAmountIrr: 5_000_000m,
            discountType: DiscountType.FixedAmountIrr,
            discountValue: 8_000_000m,
            maxDiscountAmountIrr: null);

        fixedOverflow.Should().Be(5_000_000m);
    }

    [Fact]
    public void RentalPricing_DailyAndMonthlyUnits_CalculatesExactIrrFromInstrumentRecord()
    {
        var guitar = new Instrument
        {
            Name = "گیتار کلاسیک",
            Slug = "guitar",
            DailyRentalPriceIrr = 450_000m,
            MonthlyRentalPriceIrr = 9_500_000m
        };

        InstrumentRentalService.CalculateRentalPriceIrr(guitar, RentalPricingUnit.Daily, 10)
            .Should().Be(4_500_000m);
        InstrumentRentalService.CalculateRentalPriceIrr(guitar, RentalPricingUnit.Monthly, 2)
            .Should().Be(19_000_000m);
    }

    [Fact]
    public void CurrencyFormatter_ConvertsInternalIrrToPersianTomanOnlyAtDisplayBoundary()
    {
        var formatted = CurrencyFormatter.FormatToman(25_000_000m, usePersianDigits: false);
        formatted.Should().Be("2,500,000 تومان");
    }
}
