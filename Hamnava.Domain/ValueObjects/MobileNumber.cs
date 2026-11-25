using System.Text.RegularExpressions;

namespace Hamnava.Domain.ValueObjects;

/// <summary>
/// Normalized Iranian mobile number in canonical 11-digit format (09XXXXXXXXX).
/// Supports Persian/Arabic numerals, +98, 0098, and 98 prefixes.
/// </summary>
public sealed partial record MobileNumber
{
    public string Value { get; }

    private MobileNumber(string normalizedValue)
    {
        Value = normalizedValue;
    }

    public static MobileNumber Parse(string raw)
    {
        if (!TryParse(raw, out var result) || result is null)
        {
            throw new ArgumentException("شماره موبایل واردشده معتبر نیست.", nameof(raw));
        }

        return result;
    }

    public static bool TryParse(string? raw, out MobileNumber? result)
    {
        result = null;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        var normalizedDigits = ConvertPersianAndArabicDigitsToLatin(raw.Trim());
        var cleaned = NonDigitRegex().Replace(normalizedDigits, string.Empty);

        if (cleaned.StartsWith("0098", StringComparison.Ordinal))
        {
            cleaned = "0" + cleaned[4..];
        }
        else if (cleaned.StartsWith("98", StringComparison.Ordinal) && cleaned.Length == 12)
        {
            cleaned = "0" + cleaned[2..];
        }
        else if (cleaned.StartsWith('9') && cleaned.Length == 10)
        {
            cleaned = "0" + cleaned;
        }

        if (!IranMobileRegex().IsMatch(cleaned))
        {
            return false;
        }

        result = new MobileNumber(cleaned);
        return true;
    }

    public string ToMasked() =>
        Value.Length == 11 ? $"{Value[..4]}***{Value[7..]}" : "***********";

    public override string ToString() => Value;

    private static string ConvertPersianAndArabicDigitsToLatin(string input)
    {
        var chars = input.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            chars[i] = chars[i] switch
            {
                >= '\u06F0' and <= '\u06F9' => (char)('0' + (chars[i] - '\u06F0')),
                >= '\u0660' and <= '\u0669' => (char)('0' + (chars[i] - '\u0660')),
                _ => chars[i]
            };
        }

        return new string(chars);
    }

    [GeneratedRegex(@"[^\d]")]
    private static partial Regex NonDigitRegex();

    [GeneratedRegex(@"^09\d{9}$")]
    private static partial Regex IranMobileRegex();
}
