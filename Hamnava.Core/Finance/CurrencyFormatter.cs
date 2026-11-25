using System.Globalization;

namespace Hamnava.Core.Finance;

/// <summary>
/// Centralized currency formatter. All database values are stored strictly in IRR (Rials).
/// Display conversions to Toman happen exclusively through this utility.
/// </summary>
public static class CurrencyFormatter
{
    public static string FormatToman(decimal amountIrr, bool usePersianDigits = true, bool includeUnit = true)
    {
        var tomans = decimal.Round(amountIrr / 10m, 0, MidpointRounding.AwayFromZero);
        var formatted = tomans.ToString("N0", CultureInfo.InvariantCulture);
        if (usePersianDigits)
        {
            formatted = ToPersianDigits(formatted);
        }

        return includeUnit ? $"{formatted} تومان" : formatted;
    }

    public static string FormatIrr(decimal amountIrr, bool usePersianDigits = true, bool includeUnit = true)
    {
        var rials = decimal.Round(amountIrr, 0, MidpointRounding.AwayFromZero);
        var formatted = rials.ToString("N0", CultureInfo.InvariantCulture);
        if (usePersianDigits)
        {
            formatted = ToPersianDigits(formatted);
        }

        return includeUnit ? $"{formatted} ریال" : formatted;
    }

    public static decimal ConvertTomanInputToIrr(decimal tomans) =>
        decimal.Round(tomans * 10m, 0, MidpointRounding.AwayFromZero);

    private static string ToPersianDigits(string value)
    {
        var chars = value.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            if (chars[i] is >= '0' and <= '9')
            {
                chars[i] = (char)('\u06F0' + (chars[i] - '0'));
            }
        }

        return new string(chars);
    }
}
