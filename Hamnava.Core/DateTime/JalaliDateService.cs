using System.Globalization;

namespace Hamnava.Core.DateTime;

public interface IJalaliDateService
{
    System.DateTime UtcNow { get; }
    System.DateTime TehranNow { get; }
    DateOnly TehranToday { get; }
    TimeZoneInfo AcademyTimeZone { get; }

    System.DateTime ToTehranTime(System.DateTime utcDateTime);
    System.DateTime ToUtcFromTehran(System.DateTime tehranLocalDateTime);

    string FormatDate(DateOnly gregorianDate, bool usePersianDigits = true);
    string FormatDate(System.DateTime utcDateTime, bool usePersianDigits = true);
    string FormatDateTime(System.DateTime utcDateTime, bool usePersianDigits = true);
    string FormatTime(System.DateTime utcDateTime, bool usePersianDigits = true);
    string GetPersianDayOfWeek(DayOfWeek dayOfWeek);
    string GetPersianMonthName(int jalaliMonth);

    bool TryParseJalaliDate(string jalaliDate, out DateOnly gregorianDate);
    DateOnly ParseJalaliDate(string jalaliDate);
    (int Year, int Month, int Day) ToJalaliParts(DateOnly gregorianDate);
    DateOnly FromJalaliParts(int jalaliYear, int jalaliMonth, int jalaliDay);
    (DateOnly StartGregorian, DateOnly EndGregorian) GetJalaliMonthRange(int jalaliYear, int jalaliMonth);
    bool IsBirthdayMatch(DateOnly birthDateGregorian, DateOnly targetDateGregorian);
    string ToPersianDigits(string input);
    string ToLatinDigits(string input);
}

public sealed class JalaliDateService : IJalaliDateService
{
    private static readonly PersianCalendar Calendar = new();
    private static readonly string[] PersianMonths =
    [
        "فروردین", "اردیبهشت", "خرداد", "تیر", "مرداد", "شهریور",
        "مهر", "آبان", "آذر", "دی", "بهمن", "اسفند"
    ];

    private readonly TimeProvider _timeProvider;

    public TimeZoneInfo AcademyTimeZone { get; }

    public JalaliDateService(TimeProvider? timeProvider = null, string timeZoneId = "Asia/Tehran")
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
        AcademyTimeZone = ResolveTimeZone(timeZoneId);
    }

    public System.DateTime UtcNow => _timeProvider.GetUtcNow().UtcDateTime;

    public System.DateTime TehranNow => ToTehranTime(UtcNow);

    public DateOnly TehranToday => DateOnly.FromDateTime(TehranNow);

    public System.DateTime ToTehranTime(System.DateTime utcDateTime)
    {
        var normalized = System.DateTime.SpecifyKind(utcDateTime, DateTimeKind.Utc);
        return TimeZoneInfo.ConvertTimeFromUtc(normalized, AcademyTimeZone);
    }

    public System.DateTime ToUtcFromTehran(System.DateTime tehranLocalDateTime)
    {
        var unspecified = System.DateTime.SpecifyKind(tehranLocalDateTime, DateTimeKind.Unspecified);
        return TimeZoneInfo.ConvertTimeToUtc(unspecified, AcademyTimeZone);
    }

    public string FormatDate(DateOnly gregorianDate, bool usePersianDigits = true)
    {
        var (y, m, d) = ToJalaliParts(gregorianDate);
        var formatted = $"{y:0000}/{m:00}/{d:00}";
        return usePersianDigits ? ToPersianDigits(formatted) : formatted;
    }

    public string FormatDate(System.DateTime utcDateTime, bool usePersianDigits = true) =>
        FormatDate(DateOnly.FromDateTime(ToTehranTime(utcDateTime)), usePersianDigits);

    public string FormatDateTime(System.DateTime utcDateTime, bool usePersianDigits = true)
    {
        var tehran = ToTehranTime(utcDateTime);
        var date = FormatDate(DateOnly.FromDateTime(tehran), false);
        var formatted = $"{date} - {tehran:HH:mm}";
        return usePersianDigits ? ToPersianDigits(formatted) : formatted;
    }

    public string FormatTime(System.DateTime utcDateTime, bool usePersianDigits = true)
    {
        var tehran = ToTehranTime(utcDateTime);
        var formatted = $"{tehran:HH:mm}";
        return usePersianDigits ? ToPersianDigits(formatted) : formatted;
    }

    public string GetPersianDayOfWeek(DayOfWeek dayOfWeek) => dayOfWeek switch
    {
        DayOfWeek.Saturday => "شنبه",
        DayOfWeek.Sunday => "یک‌شنبه",
        DayOfWeek.Monday => "دوشنبه",
        DayOfWeek.Tuesday => "سه‌شنبه",
        DayOfWeek.Wednesday => "چهارشنبه",
        DayOfWeek.Thursday => "پنج‌شنبه",
        DayOfWeek.Friday => "جمعه",
        _ => string.Empty
    };

    public string GetPersianMonthName(int jalaliMonth) =>
        jalaliMonth is >= 1 and <= 12 ? PersianMonths[jalaliMonth - 1] : string.Empty;

    public bool TryParseJalaliDate(string jalaliDate, out DateOnly gregorianDate)
    {
        gregorianDate = default;
        if (string.IsNullOrWhiteSpace(jalaliDate))
        {
            return false;
        }

        var normalized = ToLatinDigits(jalaliDate.Trim()).Replace('-', '/');
        var parts = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 3 ||
            !int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var year) ||
            !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var month) ||
            !int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var day))
        {
            return false;
        }

        try
        {
            gregorianDate = FromJalaliParts(year, month, day);
            return true;
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }
    }

    public DateOnly ParseJalaliDate(string jalaliDate)
    {
        if (!TryParseJalaliDate(jalaliDate, out var result))
        {
            throw new FormatException("فرمت تاریخ شمسی معتبر نیست. قالب صحیح: YYYY/MM/DD");
        }

        return result;
    }

    public (int Year, int Month, int Day) ToJalaliParts(DateOnly gregorianDate)
    {
        var dt = gregorianDate.ToDateTime(TimeOnly.MinValue);
        return (Calendar.GetYear(dt), Calendar.GetMonth(dt), Calendar.GetDayOfMonth(dt));
    }

    public DateOnly FromJalaliParts(int jalaliYear, int jalaliMonth, int jalaliDay)
    {
        var dt = Calendar.ToDateTime(jalaliYear, jalaliMonth, jalaliDay, 0, 0, 0, 0);
        return DateOnly.FromDateTime(dt);
    }

    public (DateOnly StartGregorian, DateOnly EndGregorian) GetJalaliMonthRange(int jalaliYear, int jalaliMonth)
    {
        var daysInMonth = Calendar.GetDaysInMonth(jalaliYear, jalaliMonth);
        var start = FromJalaliParts(jalaliYear, jalaliMonth, 1);
        var end = FromJalaliParts(jalaliYear, jalaliMonth, daysInMonth);
        return (start, end);
    }

    public bool IsBirthdayMatch(DateOnly birthDateGregorian, DateOnly targetDateGregorian)
    {
        var (_, birthMonth, birthDay) = ToJalaliParts(birthDateGregorian);
        var (targetYear, targetMonth, targetDay) = ToJalaliParts(targetDateGregorian);

        // Handle Esfand 30 leap-year birthdays on non-leap years as Esfand 29
        if (birthMonth == 12 && birthDay == 30 && !Calendar.IsLeapYear(targetYear))
        {
            return targetMonth == 12 && targetDay == 29;
        }

        return birthMonth == targetMonth && birthDay == targetDay;
    }

    public string ToPersianDigits(string input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return input;
        }

        var chars = input.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            if (chars[i] is >= '0' and <= '9')
            {
                chars[i] = (char)('\u06F0' + (chars[i] - '0'));
            }
        }

        return new string(chars);
    }

    public string ToLatinDigits(string input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return input;
        }

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

    private static TimeZoneInfo ResolveTimeZone(string timeZoneId)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        }
        catch (TimeZoneNotFoundException)
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById("Iran Standard Time");
            }
            catch (TimeZoneNotFoundException)
            {
                return TimeZoneInfo.CreateCustomTimeZone("IranStd", TimeSpan.FromHours(3.5), "Iran Standard Time", "Iran Standard Time");
            }
        }
    }
}
