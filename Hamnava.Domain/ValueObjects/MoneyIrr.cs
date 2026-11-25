using Hamnava.Domain.Enums;

namespace Hamnava.Domain.ValueObjects;

/// <summary>
/// Authoritative internal monetary value strictly denominated in Iranian Rials (IRR).
/// Never mix Rial and Toman internally; use ToToman() strictly for display formatting.
/// </summary>
public readonly record struct MoneyIrr
{
    public decimal AmountIrr { get; }

    public MoneyIrr(decimal amountIrr)
    {
        AmountIrr = decimal.Round(amountIrr, 0, MidpointRounding.AwayFromZero);
    }

    public static MoneyIrr Zero => new(0m);

    public static MoneyIrr FromIrr(decimal rials) => new(rials);

    public static MoneyIrr FromToman(decimal tomans) => new(tomans * 10m);

    public decimal ToToman() => AmountIrr / 10m;

    public MoneyIrr ApplyPercentage(decimal percentage, FinancialRoundingMode roundingMode)
    {
        if (percentage < 0m || percentage > 100m)
        {
            throw new ArgumentOutOfRangeException(nameof(percentage), "درصد باید بین ۰ تا ۱۰۰ باشد.");
        }

        var raw = AmountIrr * (percentage / 100m);
        return Round(raw, roundingMode);
    }

    public static MoneyIrr Round(decimal rawIrr, FinancialRoundingMode roundingMode)
    {
        var rounded = roundingMode switch
        {
            FinancialRoundingMode.AwayFromZeroToNearest1000Irr =>
                Math.Round(rawIrr / 1000m, 0, MidpointRounding.AwayFromZero) * 1000m,
            FinancialRoundingMode.FloorTo1000Irr =>
                Math.Floor(rawIrr / 1000m) * 1000m,
            _ => Math.Round(rawIrr, 0, MidpointRounding.AwayFromZero)
        };

        return new MoneyIrr(rounded);
    }

    public static MoneyIrr operator +(MoneyIrr a, MoneyIrr b) => new(a.AmountIrr + b.AmountIrr);
    public static MoneyIrr operator -(MoneyIrr a, MoneyIrr b) => new(a.AmountIrr - b.AmountIrr);
}
