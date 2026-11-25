using Hamnava.DataLayer.Context;
using Hamnava.Domain.Constants;
using Hamnava.Domain.Entities.Cms;
using Hamnava.Domain.Entities.Education;
using Hamnava.Domain.Entities.Finance;
using Hamnava.Domain.Entities.Identity;
using Hamnava.Domain.Entities.Instruments;
using Hamnava.Domain.Entities.Media;
using Hamnava.Domain.Entities.Messaging;
using Hamnava.Domain.Enums;
using Hamnava.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace Hamnava.DataLayer.Seed;

/// <summary>
/// Idempotent database seeder for required operational baselines.
/// Never inserts fake production students or fake financial transactions.
/// </summary>
public sealed class HamnavaDbSeeder
{
    private readonly HamnavaDbContext _db;

    public HamnavaDbContext Db => _db;

    public HamnavaDbSeeder(HamnavaDbContext db)
    {
        _db = db;
    }

    public async Task SeedBaselineAsync(
        string? initialAdminMobile,
        string initialAdminFirstName = "مدیر",
        string initialAdminLastName = "سیستم",
        CancellationToken cancellationToken = default)
    {
        await SeedRolesAsync(cancellationToken);
        await SeedSystemSettingsAsync(cancellationToken);
        await SeedFinancialSettingsAsync(cancellationToken);
        await SeedInstrumentsAndRoomsAsync(cancellationToken);
        await SeedCourseAndConsultationPlansAsync(cancellationToken);
        await SeedSmsTemplatesAsync(cancellationToken);
        await SeedPublicWebsiteContentAsync(cancellationToken);
        await SeedDefaultStoryTemplateAsync(cancellationToken);

        if (!string.IsNullOrWhiteSpace(initialAdminMobile))
        {
            await SeedInitialAdminAsync(initialAdminMobile, initialAdminFirstName, initialAdminLastName, cancellationToken);
        }
    }

    private async Task SeedRolesAsync(CancellationToken ct)
    {
        var desired = new (string Name, string DisplayFa, string Desc)[]
        {
            (RoleNames.Admin, "مدیر سیستم", "دسترسی کامل مدیریتی و عملیاتی به تمام بخش‌های هم‌نوا"),
            (RoleNames.Teacher, "مدرس", "مدیریت هنرجویان اختصاصی، تقویم، حضور و غیاب، تمرین، پورتفولیو و کیف پول"),
            (RoleNames.Student, "هنرجو", "مشاهده تقویم، سهمیه‌ها، تمرین‌ها، پرداخت‌ها، اجاره ساز و پورتفولیو"),
            (RoleNames.Referrer, "معرف", "مدیریت کد معرفی، پیگیری هنرجویان معرفی‌شده و کیف پول پورسانت")
        };

        var existingNames = await _db.Roles.Select(r => r.Name).ToListAsync(ct);
        foreach (var (name, displayFa, desc) in desired)
        {
            if (!existingNames.Contains(name))
            {
                _db.Roles.Add(new Role
                {
                    Name = name,
                    DisplayNameFa = displayFa,
                    Description = desc,
                    IsSystemRole = true
                });
            }
        }

        await _db.SaveChangesAsync(ct);
    }

    private async Task SeedSystemSettingsAsync(CancellationToken ct)
    {
        var defaults = new (string Key, string Value, string Category, string Type, string LabelFa, string DescFa)[]
        {
            (SystemSettingKeys.QuotaPeriodDays, "60", "Quota", "int", "طول دوره سهمیه (روز)", "تعداد روزهای هر دوره ثابت سهمیه از تاریخ شروع سهمیه هنرجو"),
            (SystemSettingKeys.QuotaMaxAuthorizedAbsences, "1", "Quota", "int", "حداکثر غیبت موجه در هر دوره", "تعداد مجاز ثبت غیبت موجه توسط هنرجو در هر دوره سهمیه"),
            (SystemSettingKeys.QuotaMaxRescheduleRequests, "2", "Quota", "int", "حداکثر درخواست جابجایی در هر دوره", "تعداد مجاز درخواست تغییر زمان جلسه توسط هنرجو"),
            (SystemSettingKeys.QuotaMaxMakeupSelections, "2", "Quota", "int", "حداکثر انتخاب جلسه جبرانی در هر دوره", "تعداد مجاز انتخاب جلسه جبرانی مشروط به واجد شرایط بودن"),
            (SystemSettingKeys.QuotaMinimumAbsenceNoticeHours, "24", "Quota", "int", "حداقل مهلت اعلام غیبت موجه (ساعت)", "حداقل فاصله زمانی به ساعت پیش از شروع کلاس بر اساس زمان سرور"),
            (SystemSettingKeys.RescheduleRequiresTeacherApproval, "true", "Scheduling", "bool", "نیاز به تایید مدرس برای جابجایی", "آیا درخواست جابجایی هنرجو نیازمند تایید مدرس است یا خودکار ثبت شود"),
            (SystemSettingKeys.HolidayAutoCreateMakeupEligibility, "true", "Scheduling", "bool", "ایجاد حق جبرانی در تعطیلات رسمی", "آیا ثبت وضعیت تعطیل رسمی حق انتخاب جلسه جبرانی ایجاد کند"),

            (SystemSettingKeys.OtpCodeLength, "4", "Security", "int", "طول کد یک‌بارمصرف", "تعداد ارقام کد تایید پیامکی"),
            (SystemSettingKeys.OtpExpirationSeconds, "180", "Security", "int", "مدت اعتبار کد تایید (ثانیه)", "زمان انقضای کد OTP"),
            (SystemSettingKeys.OtpResendCooldownSeconds, "60", "Security", "int", "فاصله ارسال مجدد کد (ثانیه)", "حداقل زمان انتظار بین دو درخواست کد تایید"),
            (SystemSettingKeys.OtpMaxVerificationAttempts, "5", "Security", "int", "حداکثر تلاش ناموفق کد تایید", "تعداد دفعات مجاز ورود اشتباه پیش از باطل شدن کد"),
            (SystemSettingKeys.OtpMaxDailyRequestsPerMobile, "10", "Security", "int", "حداکثر درخواست روزانه به ازای شماره", "سقف ارسال کد در ۲۴ ساعت برای یک شماره موبایل"),
            (SystemSettingKeys.OtpMaxRequestsPerIpPerHour, "20", "Security", "int", "حداکثر درخواست ساعتی به ازای IP", "محدودیت نرخ درخواست OTP برای هر آدرس IP"),
            (SystemSettingKeys.AuthSessionExpirationHours, "168", "Security", "int", "مدت اعتبار نشست کاربری (ساعت)", "مدت زمان ماندگاری کوکی احراز هویت"),

            (SystemSettingKeys.BirthdayDiscountEnabled, "true", "Birthday", "bool", "فعال بودن تخفیف تولد", "صدور خودکار کد تخفیف در روز تولد هنرجو"),
            (SystemSettingKeys.BirthdayDiscountType, "Percentage", "Birthday", "string", "نوع تخفیف تولد", "Percentage یا FixedAmountIrr"),
            (SystemSettingKeys.BirthdayDiscountValue, "15", "Birthday", "decimal", "مقدار تخفیف تولد", "درصد یا مبلغ ریالی تخفیف تولد"),
            (SystemSettingKeys.BirthdayDiscountMaxAmountIrr, "5000000", "Birthday", "decimal", "سقف ریالی تخفیف تولد", "حداکثر مبلغ تخفیف تولد به ریال"),
            (SystemSettingKeys.BirthdayDiscountValidityDays, "14", "Birthday", "int", "مدت اعتبار کد تخفیف تولد (روز)", "تعداد روزهای اعتبار کد تخفیف تولد پس از صدور"),

            (SystemSettingKeys.LessonReminderIntervalsHours, "24,2", "Notifications", "string", "فواصل یادآوری کلاس (ساعت)", "لیست ساعت‌های پیش از شروع کلاس جداشده با ویرگول"),
            (SystemSettingKeys.RentalExpirationReminderDays, "3,1", "Notifications", "string", "فواصل یادآوری پایان اجاره ساز (روز)", "تعداد روز مانده به انقضای اجاره برای ارسال یادآور"),

            (SystemSettingKeys.MediaAllowedImageExtensions, ".jpg,.jpeg,.png,.webp", "Media", "string", "پسوندهای مجاز تصویر", "فرمت‌های مجاز آپلود تصویر در پورتفولیو"),
            (SystemSettingKeys.MediaAllowedVideoExtensions, ".mp4,.mov", "Media", "string", "پسوندهای مجاز ویدیو", "فرمت‌های مجاز آپلود ویدیو برای پردازش FFmpeg"),
            (SystemSettingKeys.MediaMaxImageSizeMb, "10", "Media", "int", "حداکثر حجم تصویر (مگابایت)", "سقف حجم فایل تصویری"),
            (SystemSettingKeys.MediaMaxVideoSizeMb, "250", "Media", "int", "حداکثر حجم ویدیو (مگابایت)", "سقف حجم فایل ویدیویی"),
            (SystemSettingKeys.MediaConvertImagesToWebP, "true", "Media", "bool", "تبدیل خودکار تصاویر به WebP", "تولید نسخه بهینه WebP برای تصاویر پورتفولیو"),

            (SystemSettingKeys.TeacherCanCreateUnlimitedDiscounts, "true", "Teacher", "bool", "امکان ایجاد کد تخفیف توسط مدرس", "اجازه تعریف کد تخفیف اختصاصی توسط مدرس برای هنرجویان خودش"),
            (SystemSettingKeys.TeacherCanToggleStudentPublicVisibility, "true", "Teacher", "bool", "مدیریت نمایش عمومی توسط مدرس", "منوط به ثبت رضایت‌نامه حریم خصوصی هنرجو"),
            (SystemSettingKeys.TeacherCanResetOwnStudentQuotas, "true", "Teacher", "bool", "امکان بازنشانی سهمیه توسط مدرس", "اجازه ریست سهمیه هنرجویان اختصاصی توسط مدرس"),
            (SystemSettingKeys.CertificateMinCompletedSessions, "4", "Education", "int", "حداقل جلسات تکمیل‌شده برای گواهی‌نامه", "شرط صدور گواهی‌نامه PDF دوره")
        };

        var existingKeys = await _db.SystemSettings.Select(s => s.Key).ToListAsync(ct);
        foreach (var item in defaults)
        {
            if (!existingKeys.Contains(item.Key))
            {
                _db.SystemSettings.Add(new SystemSetting
                {
                    Key = item.Key,
                    Value = item.Value,
                    Category = item.Category,
                    DataType = item.Type,
                    DisplayLabelFa = item.LabelFa,
                    DescriptionFa = item.DescFa,
                    IsEditableInAdminPanel = true
                });
            }
        }

        await _db.SaveChangesAsync(ct);
    }

    private async Task SeedFinancialSettingsAsync(CancellationToken ct)
    {
        if (!await _db.FinancialSettings.AnyAsync(f => f.IsCurrent, ct))
        {
            _db.FinancialSettings.Add(new FinancialSetting
            {
                DefaultTeacherCommissionPercentage = 60m,
                DefaultAcademyCommissionPercentage = 40m,
                DefaultReferrerCommissionType = ReferrerCommissionType.Percentage,
                DefaultReferrerCommissionValue = 5m,
                DeductReferrerShareFromAcademyShare = true,
                CalculationBasis = CommissionCalculationBasis.PostDiscountFinalAmount,
                RoundingMode = FinancialRoundingMode.AwayFromZeroToNearest1000Irr,
                StudentReferralRewardType = ReferralRewardType.DiscountPercentage,
                StudentReferralRewardValue = 50m,
                StudentReferralRewardValidityDays = 90,
                SettleCommissionOnUnauthorizedAbsence = true,
                SettleCommissionOnAuthorizedAbsence = false,
                MinimumWithdrawalAmountIrr = 1_000_000m,
                AllowNegativeWalletBalance = false,
                IsCurrent = true
            });

            await _db.SaveChangesAsync(ct);
        }
    }

    private async Task SeedInstrumentsAndRoomsAsync(CancellationToken ct)
    {
        if (await _db.InstrumentCategories.AnyAsync(ct))
        {
            return;
        }

        var catStrings = new InstrumentCategory { Name = "سازهای زهی و آرشه‌ای", Slug = "strings", DisplayOrder = 1 };
        var catKeys = new InstrumentCategory { Name = "سازهای کلیدی", Slug = "keyboards", DisplayOrder = 2 };
        var catIranian = new InstrumentCategory { Name = "سازهای ایرانی", Slug = "iranian", DisplayOrder = 3 };
        _db.InstrumentCategories.AddRange(catStrings, catKeys, catIranian);
        await _db.SaveChangesAsync(ct);

        var instruments = new List<Instrument>
        {
            new() { CategoryId = catStrings.Id, Name = "ویولن", Slug = "violin", IsRentable = true, DailyRentalPriceIrr = 200_000m, MonthlyRentalPriceIrr = 4_500_000m, TotalInventoryQuantity = 3, AvailableInventoryQuantity = 3, DisplayOrder = 1 },
            new() { CategoryId = catKeys.Id, Name = "پیانو", Slug = "piano", IsRentable = false, DailyRentalPriceIrr = 0m, MonthlyRentalPriceIrr = 0m, TotalInventoryQuantity = 2, AvailableInventoryQuantity = 2, DisplayOrder = 2 },
            new() { CategoryId = catStrings.Id, Name = "گیتار باس", Slug = "bass-guitar", IsRentable = true, DailyRentalPriceIrr = 250_000m, MonthlyRentalPriceIrr = 5_000_000m, TotalInventoryQuantity = 2, AvailableInventoryQuantity = 2, DisplayOrder = 3 },
            new() { CategoryId = catStrings.Id, Name = "گیتار کلاسیک", Slug = "classical-guitar", IsRentable = true, DailyRentalPriceIrr = 180_000m, MonthlyRentalPriceIrr = 3_800_000m, TotalInventoryQuantity = 4, AvailableInventoryQuantity = 4, DisplayOrder = 4 },
            new() { CategoryId = catStrings.Id, Name = "گیتار الکتریک", Slug = "electric-guitar", IsRentable = true, DailyRentalPriceIrr = 250_000m, MonthlyRentalPriceIrr = 5_500_000m, TotalInventoryQuantity = 2, AvailableInventoryQuantity = 2, DisplayOrder = 5 },
            new() { CategoryId = catKeys.Id, Name = "کیبورد", Slug = "keyboard", IsRentable = true, DailyRentalPriceIrr = 220_000m, MonthlyRentalPriceIrr = 4_800_000m, TotalInventoryQuantity = 2, AvailableInventoryQuantity = 2, DisplayOrder = 6 },
            new() { CategoryId = catIranian.Id, Name = "سنتور", Slug = "santoor", IsRentable = true, DailyRentalPriceIrr = 180_000m, MonthlyRentalPriceIrr = 3_800_000m, TotalInventoryQuantity = 3, AvailableInventoryQuantity = 3, DisplayOrder = 7 },
            new() { CategoryId = catIranian.Id, Name = "تار", Slug = "tar", IsRentable = true, DailyRentalPriceIrr = 200_000m, MonthlyRentalPriceIrr = 4_200_000m, TotalInventoryQuantity = 2, AvailableInventoryQuantity = 2, DisplayOrder = 8 },
            new() { CategoryId = catIranian.Id, Name = "سه‌تار", Slug = "setar", IsRentable = true, DailyRentalPriceIrr = 150_000m, MonthlyRentalPriceIrr = 3_200_000m, TotalInventoryQuantity = 3, AvailableInventoryQuantity = 3, DisplayOrder = 9 }
        };
        _db.Instruments.AddRange(instruments);
        await _db.SaveChangesAsync(ct);

        var room1 = new Room { Name = "کلاس ۱ (اصلی)", Description = "اتاق آموزش مجهز به پیانو و آکوستیک استاندارد", Capacity = 2, DisplayOrder = 1 };
        var room2 = new Room { Name = "کلاس ۲ (زهی و سنتی)", Description = "مناسب آموزش سازهای زهی، گیتار و ایرانی", Capacity = 2, DisplayOrder = 2 };
        _db.Rooms.AddRange(room1, room2);
        await _db.SaveChangesAsync(ct);

        foreach (var inst in instruments)
        {
            _db.RoomSupportedInstruments.Add(new RoomSupportedInstrument { RoomId = room1.Id, InstrumentId = inst.Id });
            if (inst.Slug != "piano")
            {
                _db.RoomSupportedInstruments.Add(new RoomSupportedInstrument { RoomId = room2.Id, InstrumentId = inst.Id });
            }
        }

        await _db.SaveChangesAsync(ct);
    }

    private async Task SeedCourseAndConsultationPlansAsync(CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        if (!await _db.CoursePlans.AnyAsync(ct))
        {
            _db.CoursePlans.AddRange(
                new CoursePlan
                {
                    Title = "دوره یک‌ماهه ۲۰ دقیقه‌ای",
                    Subtitle = "۴ جلسه تمرکزی در ماه",
                    Description = "مناسب کودکان و هنرجویان پرمشغله برای تثبیت عادت تمرین منظم.",
                    LessonDurationMinutes = 20,
                    CoursePeriodMonths = 1,
                    SessionCount = 4,
                    BasePriceIrr = 6_000_000m,
                    PublicDisplayPriceIrr = 4_500_000m,
                    IsDiscountEligible = true,
                    DisplayOrder = 1,
                    EffectiveFromGregorian = today
                },
                new CoursePlan
                {
                    Title = "دوره یک‌ماهه ۴۰ دقیقه‌ای",
                    Subtitle = "۴ جلسه استاندارد در ماه",
                    Description = "استانداردترین برنامه آموزشی برای یادگیری عمیق تکنیک و اجرای قطعات.",
                    LessonDurationMinutes = 40,
                    CoursePeriodMonths = 1,
                    SessionCount = 4,
                    BasePriceIrr = 12_000_000m,
                    PublicDisplayPriceIrr = 6_750_000m,
                    IsDiscountEligible = true,
                    IsFeatured = true,
                    BadgeText = "محبوب‌ترین انتخاب",
                    DisplayOrder = 2,
                    EffectiveFromGregorian = today
                },
                new CoursePlan
                {
                    Title = "دوره سه‌ماهه ۴۰ دقیقه‌ای",
                    Subtitle = "۱۲ جلسه مستمر با برنامه‌ریزی فصلی",
                    Description = "مناسب پیشرفت پیوسته با صرفه‌جویی اقتصادی در شهریه سه‌ماهه.",
                    LessonDurationMinutes = 40,
                    CoursePeriodMonths = 3,
                    SessionCount = 12,
                    BasePriceIrr = 32_000_000m,
                    PublicDisplayPriceIrr = 28_000_000m,
                    IsDiscountEligible = true,
                    DisplayOrder = 3,
                    EffectiveFromGregorian = today
                },
                new CoursePlan
                {
                    Title = "دوره ویژه (VIP)",
                    Subtitle = "۸ جلسه ۴۰ دقیقه‌ای ماهانه با پشتیبانی ویژه",
                    Description = "برنامه جامع دو جلسه در هفته با پیگیری مستمر تمرین‌ها.",
                    LessonDurationMinutes = 40,
                    CoursePeriodMonths = 1,
                    SessionCount = 8,
                    BasePriceIrr = 40_000_000m,
                    PublicDisplayPriceIrr = 20_000_000m,
                    IsDiscountEligible = true,
                    BadgeText = "انتخاب حرفه‌ای‌ها",
                    DisplayOrder = 4,
                    EffectiveFromGregorian = today
                }
            );
            await _db.SaveChangesAsync(ct);
        }

        if (!await _db.ConsultationPlans.AnyAsync(ct))
        {
            _db.ConsultationPlans.AddRange(
                new ConsultationPlan
                {
                    Title = "مشاوره کوتاه استعدادیابی و انتخاب ساز (۱۰ دقیقه)",
                    Description = "جلسه ۱۰ دقیقه‌ای برای آشنایی اولیه و راهنمایی انتخاب ساز مناسب.",
                    DurationMinutes = 10,
                    PriceIrr = 0m,
                    MaxBookingsPerSlot = 1,
                    DisplayOrder = 1
                },
                new ConsultationPlan
                {
                    Title = "مشاوره تخصصی سطح‌سنجی و خرید ساز (۲۰ دقیقه)",
                    Description = "بررسی دقیق سطح نوازندگی، برنامه آموزشی اختصاصی و مشاوره خرید ساز استاندارد.",
                    DurationMinutes = 20,
                    PriceIrr = 2_000_000m,
                    MaxBookingsPerSlot = 1,
                    DisplayOrder = 2
                }
            );
            await _db.SaveChangesAsync(ct);
        }
    }

    private async Task SeedSmsTemplatesAsync(CancellationToken ct)
    {
        if (await _db.SmsTemplates.AnyAsync(ct))
        {
            return;
        }

        _db.SmsTemplates.AddRange(
            new SmsTemplate { Code = "OTP_LOGIN", Title = "کد تایید ورود", TriggerType = SmsTriggerType.OtpVerification, BodyTemplate = "کد ورود شما به سامانه آموزشگاه موسیقی هم‌نوا: {Code}\nلغو11", AllowedPlaceholdersCsv = "Code", ProviderPatternCode = "2868" },
            new SmsTemplate { Code = "ENROLLMENT_CONFIRMED", Title = "تایید ثبت‌نام دوره", TriggerType = SmsTriggerType.EnrollmentConfirmed, BodyTemplate = "{StudentName} عزیز، ثبت‌نام شما در دوره {PlanTitle} با استاد {TeacherName} تایید شد. هم‌نوا", AllowedPlaceholdersCsv = "StudentName,PlanTitle,TeacherName,StartDate" },
            new SmsTemplate { Code = "PAYMENT_CONFIRMED", Title = "تایید پرداخت موفق", TriggerType = SmsTriggerType.PaymentConfirmed, BodyTemplate = "{UserName} عزیز، پرداخت شما به مبلغ {AmountToman} تومان با کد پیگیری {RefId} با موفقیت ثبت شد. هم‌نوا", AllowedPlaceholdersCsv = "UserName,AmountToman,RefId" },
            new SmsTemplate { Code = "LESSON_REMINDER", Title = "یادآوری جلسه کلاس", TriggerType = SmsTriggerType.LessonReminder, BodyTemplate = "سلام {StudentName} عزیز، یادآوری کلاس {InstrumentName} شما با استاد {TeacherName} در تاریخ {LessonDate} ساعت {LessonTime}. هم‌نوا", AllowedPlaceholdersCsv = "StudentName,InstrumentName,TeacherName,LessonDate,LessonTime,RoomName" },
            new SmsTemplate { Code = "ABSENCE_CONFIRMED", Title = "تایید غیبت موجه", TriggerType = SmsTriggerType.AuthorizedAbsenceConfirmed, BodyTemplate = "{StudentName} عزیز، درخواست غیبت موجه شما برای جلسه {LessonDate} ثبت شد. می‌توانید از پنل خود جلسه جبرانی انتخاب کنید. هم‌نوا", AllowedPlaceholdersCsv = "StudentName,LessonDate,LessonTime" },
            new SmsTemplate { Code = "RESCHEDULE_CONFIRMED", Title = "تایید تغییر زمان کلاس", TriggerType = SmsTriggerType.RescheduleConfirmed, BodyTemplate = "{StudentName} عزیز، جلسه شما به تاریخ {NewDate} ساعت {NewTime} منتقل شد. هم‌نوا", AllowedPlaceholdersCsv = "StudentName,OldDate,NewDate,NewTime" },
            new SmsTemplate { Code = "MAKEUP_CONFIRMED", Title = "تایید جلسه جبرانی", TriggerType = SmsTriggerType.MakeupConfirmed, BodyTemplate = "{StudentName} عزیز، جلسه جبرانی شما برای تاریخ {LessonDate} ساعت {LessonTime} ثبت شد. هم‌نوا", AllowedPlaceholdersCsv = "StudentName,LessonDate,LessonTime,RoomName" },
            new SmsTemplate { Code = "BIRTHDAY_GREETING", Title = "تبریک تولد و کد تخفیف", TriggerType = SmsTriggerType.BirthdayGreeting, BodyTemplate = "{StudentName} عزیز، تولدتان مبارک! هدیه آموزشگاه موسیقی هم‌نوا به شما کد تخفیف {DiscountCode} تا تاریخ {ExpiryDate}.", AllowedPlaceholdersCsv = "StudentName,DiscountCode,ExpiryDate" },
            new SmsTemplate { Code = "REFERRAL_REWARD", Title = "اعطای پاداش معرفی دوست", TriggerType = SmsTriggerType.ReferralRewardGranted, BodyTemplate = "{ReferrerName} عزیز، به پاس معرفی هنرجوی جدید، کد تخفیف {DiscountCode} برای شما صادر شد. هم‌نوا", AllowedPlaceholdersCsv = "ReferrerName,DiscountCode,RewardDisplay,ExpiryDate" },
            new SmsTemplate { Code = "RENTAL_EXPIRY_REMINDER", Title = "یادآوری پایان اجاره ساز", TriggerType = SmsTriggerType.RentalExpirationReminder, BodyTemplate = "{StudentName} عزیز، مهلت اجاره ساز {InstrumentName} در تاریخ {EndDate} به پایان می‌رسد. جهت تمدید به پنل مراجعه کنید. هم‌نوا", AllowedPlaceholdersCsv = "StudentName,InstrumentName,EndDate,RemainingDays" }
        );
        await _db.SaveChangesAsync(ct);
    }

    private async Task SeedPublicWebsiteContentAsync(CancellationToken ct)
    {
        if (!await _db.ContactInformations.AnyAsync(ct))
        {
            _db.ContactInformations.Add(new ContactInformation
            {
                AcademyName = "آموزشگاه موسیقی هم‌نوا",
                PhonePrimary = "031-00000000",
                Email = "info@hamnavamusic.ir",
                Address = "اصفهان — از طریق پنل مدیریت قابل ویرایش است",
                WorkingHours = "شنبه تا پنج‌شنبه ۹:۰۰ تا ۲۱:۰۰",
                FooterAboutText = "آموزشگاه موسیقی هم‌نوا؛ تلفیق متدهای مدرن آموزش موسیقی، اساتید مجرب و سامانه هوشمند مدیریت یادگیری.",
                CopyrightText = "تمامی حقوق مادی و معنوی متعلق به آموزشگاه موسیقی هم‌نوا است."
            });
        }

        if (!await _db.WebsitePages.AnyAsync(ct))
        {
            var homePage = new WebsitePage
            {
                Slug = "home",
                Title = "صفحه اصلی",
                MetaTitle = "آموزشگاه موسیقی هم‌نوا | آموزش تخصصی ساز و مدیریت هوشمند یادگیری",
                MetaDescription = "آموزش تخصصی پیانو، ویولن، گیتار، سنتور، تار و سه‌تار با اساتید حرفه‌ای، پنل هوشمند هنرجو و مشاوره رایگان.",
                CanonicalPath = "/",
                IsPublished = true
            };
            _db.WebsitePages.Add(homePage);
            await _db.SaveChangesAsync(ct);

            _db.WebsiteSections.AddRange(
                new WebsiteSection
                {
                    WebsitePageId = homePage.Id,
                    SectionKey = "hero",
                    EyebrowLabel = "آموزشگاه تخصصی موسیقی هم‌نوا",
                    Title = "با موسیقی هم‌نوا شوید؛ مسیر حرفه‌ای نوازندگی از اینجا آغاز می‌شود",
                    Subtitle = "آموزش اصولی ساز با اساتید برجسته، برنامه‌ریزی منظم و سامانه هوشمند پیگیری پیشرفت هنرجو.",
                    PrimaryCtaText = "رزرو مشاوره و استعدادیابی",
                    PrimaryCtaUrl = "/consultation",
                    SecondaryCtaText = "مشاهده تعرفه دوره‌ها",
                    SecondaryCtaUrl = "/pricing",
                    DisplayOrder = 1
                },
                new WebsiteSection
                {
                    WebsitePageId = homePage.Id,
                    SectionKey = "about",
                    EyebrowLabel = "درباره هم‌نوا",
                    Title = "رویکردی نوین و داده‌محور در آموزش موسیقی",
                    Subtitle = "در هم‌نوا، کیفیت تدریس با شفافیت کامل برنامه‌ریزی، دفترچه تمرین دیجیتال و آرشیو اجرایی هنرجو همراه است.",
                    PrimaryCtaText = "بیشتر بدانید",
                    PrimaryCtaUrl = "/about",
                    DisplayOrder = 2
                }
            );
        }

        if (!await _db.HomepageStatistics.AnyAsync(ct))
        {
            _db.HomepageStatistics.AddRange(
                new HomepageStatistic { Label = "هنرجوی فعال", DisplayValue = "250", Suffix = "+", IconKey = "students", DisplayOrder = 1 },
                new HomepageStatistic { Label = "استاد متخصص", DisplayValue = "12", Suffix = "+", IconKey = "teachers", DisplayOrder = 2 },
                new HomepageStatistic { Label = "ساز تخصصی", DisplayValue = "9", Suffix = "+", IconKey = "instruments", DisplayOrder = 3 },
                new HomepageStatistic { Label = "رضایت هنرجویان", DisplayValue = "98", Suffix = "%", IconKey = "satisfaction", DisplayOrder = 4 }
            );
        }

        if (!await _db.HomepageFeatures.AnyAsync(ct))
        {
            _db.HomepageFeatures.AddRange(
                new HomepageFeature { SectionGroup = "WhyHamnava", Title = "تقویم هوشمند و مدیریت شفاف جلسات", Description = "مشاهده برنامه کلاس‌ها، ثبت غیبت موجه و انتخاب جلسه جبرانی آنلاین.", IconKey = "calendar", DisplayOrder = 1 },
                new HomepageFeature { SectionGroup = "WhyHamnava", Title = "دفترچه تمرین دیجیتال و پورتفولیو", Description = "ثبت تکالیف هفتگی توسط استاد و آرشیو ویدیویی پیشرفت هنرجو.", IconKey = "notebook", DisplayOrder = 2 },
                new HomepageFeature { SectionGroup = "WhyHamnava", Title = "امکان اجاره ساز استاندارد", Description = "شروع یادگیری بدون دغدغه خرید فوری ساز با سرویس اجاره ماهانه و روزانه.", IconKey = "instrument", DisplayOrder = 3 }
            );
        }

        if (!await _db.WebsiteMenus.AnyAsync(ct))
        {
            _db.WebsiteMenus.AddRange(
                new WebsiteMenu { MenuLocation = "Header", Title = "خانه", Url = "/", DisplayOrder = 1 },
                new WebsiteMenu { MenuLocation = "Header", Title = "اساتید", Url = "/teachers", DisplayOrder = 2 },
                new WebsiteMenu { MenuLocation = "Header", Title = "پورتفولیو هنرجویان", Url = "/students", DisplayOrder = 3 },
                new WebsiteMenu { MenuLocation = "Header", Title = "شهریه و پلن‌ها", Url = "/pricing", DisplayOrder = 4 },
                new WebsiteMenu { MenuLocation = "Header", Title = "مشاوره", Url = "/consultation", DisplayOrder = 5 },
                new WebsiteMenu { MenuLocation = "Header", Title = "درباره ما", Url = "/about", DisplayOrder = 6 },
                new WebsiteMenu { MenuLocation = "Header", Title = "تماس با ما", Url = "/contact", DisplayOrder = 7 }
            );
        }

        await _db.SaveChangesAsync(ct);
    }

    private async Task SeedDefaultStoryTemplateAsync(CancellationToken ct)
    {
        if (!await _db.StoryTemplates.AnyAsync(ct))
        {
            _db.StoryTemplates.Add(new StoryTemplate
            {
                Name = "قالب کلاسیک هم‌نوا (۹:۱۶)",
                PrimaryColorHex = "#6FA7A8",
                SecondaryColorHex = "#2A6E6F",
                AccentColorHex = "#C8B3A0",
                TextColorHex = "#FFFFFF",
                IsDefault = true,
                IsActive = true
            });
            await _db.SaveChangesAsync(ct);
        }
    }

    private async Task SeedInitialAdminAsync(string rawMobile, string firstName, string lastName, CancellationToken ct)
    {
        if (!MobileNumber.TryParse(rawMobile, out var mobile) || mobile is null)
        {
            return;
        }

        var adminRole = await _db.Roles.FirstAsync(r => r.Name == RoleNames.Admin, ct);
        var adminUser = await _db.Users.FirstOrDefaultAsync(u => u.MobileNumber == mobile.Value, ct);

        if (adminUser is null)
        {
            adminUser = new User
            {
                MobileNumber = mobile.Value,
                FirstName = firstName,
                LastName = lastName,
                Status = UserStatus.Active,
                ReferralCode = $"HMN{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}"
            };
            _db.Users.Add(adminUser);
            await _db.SaveChangesAsync(ct);

            _db.Wallets.Add(new Wallet { UserId = adminUser.Id });
        }

        var hasRole = await _db.UserRoles.AnyAsync(ur => ur.UserId == adminUser.Id && ur.RoleId == adminRole.Id, ct);
        if (!hasRole)
        {
            _db.UserRoles.Add(new UserRole { UserId = adminUser.Id, RoleId = adminRole.Id });
        }

        await _db.SaveChangesAsync(ct);
    }
}
