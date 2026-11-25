using System.ComponentModel.DataAnnotations;
using Hamnava.Domain.Entities.Cms;
using Hamnava.Domain.Entities.Education;
using Hamnava.Domain.Entities.Instruments;

namespace Hamnava.Web.ViewModels;

public sealed class PublicSiteLayoutData
{
    public ContactInformation Contact { get; init; } = new();
    public IReadOnlyList<WebsiteMenu> HeaderMenus { get; init; } = [];
    public IReadOnlyList<WebsiteMenu> FooterMenus { get; init; } = [];
    public IReadOnlyList<Instrument> FooterCourses { get; init; } = [];
}

public sealed class HomePageViewModel
{
    public WebsitePage? PageMeta { get; init; }
    public WebsiteSection? HeroSection { get; init; }
    public WebsiteSection? AboutSection { get; init; }
    public WebsiteSection? CtaSection { get; init; }
    public IReadOnlyList<HomepageStatistic> Statistics { get; init; } = [];
    public IReadOnlyList<HomepageFeature> WhyFeatures { get; init; } = [];
    public IReadOnlyList<WorldGalleryTabViewModel> WorldTabs { get; init; } = [];
    public IReadOnlyList<PublicTeacherCardViewModel> Teachers { get; init; } = [];
    public FeaturedStudentShowcaseViewModel? FeaturedStudent { get; init; }
    public IReadOnlyList<StudentCategoryThumbViewModel> StudentCategoryThumbs { get; init; } = [];
    public IReadOnlyList<PublicArticle> Articles { get; init; } = [];
    public IReadOnlyList<PublicEvent> Events { get; init; } = [];
    public ContactInformation Contact { get; init; } = new();
}

public sealed record WorldGalleryTabViewModel(
    string Key,
    string Label,
    string ImageUrl,
    string OverlayTitle,
    string LinkUrl);

public sealed record PublicTeacherCardViewModel(
    Guid UserId,
    string FullName,
    string Slug,
    string InstrumentRoleTitle,
    string? Bio,
    string ProfileImageUrl);

public sealed record FeaturedStudentShowcaseViewModel(
    Guid StudentId,
    string FullName,
    string EnglishSlug,
    string InstrumentBadge,
    string ExperienceSummary,
    string PosterImageUrl,
    string? VideoStreamUrl);

public sealed record StudentCategoryThumbViewModel(
    string InstrumentName,
    string ThumbnailUrl,
    string FilterUrl);

public sealed class PublicStudentPortfolioViewModel
{
    public Guid StudentUserId { get; init; }
    public string FullName { get; init; } = string.Empty;
    public string? EnglishFullName { get; init; }
    public string EnglishSlug { get; init; } = string.Empty;
    public string ProfileImageUrl { get; init; } = "/img/logo.webp";
    public string InstrumentName { get; init; } = string.Empty;
    public string TeacherName { get; init; } = string.Empty;
    public string CalculatedLearningDurationFa { get; init; } = string.Empty;
    public IReadOnlyList<PortfolioMediaItemViewModel> Videos { get; init; } = [];
    public IReadOnlyList<PortfolioMediaItemViewModel> Images { get; init; } = [];
}

public sealed record PortfolioMediaItemViewModel(
    Guid MediaId,
    string Title,
    string? Description,
    string StreamUrl,
    string? ThumbnailUrl,
    string UploadedJalaliDate,
    bool AllowDownload);

public sealed class PricingPageViewModel
{
    public IReadOnlyList<CoursePlan> Plans { get; init; } = [];
    public IReadOnlyList<Instrument> RentableInstruments { get; init; } = [];
}

public sealed class ConsultationBookingPageViewModel
{
    public IReadOnlyList<ConsultationPlan> Plans { get; init; } = [];
    public IReadOnlyList<PublicTeacherCardViewModel> Consultants { get; init; } = [];

    [Required(ErrorMessage = "انتخاب نوع مشاوره الزامی است.")]
    public Guid SelectedPlanId { get; set; }

    [Required(ErrorMessage = "انتخاب مشاور الزامی است.")]
    public Guid SelectedConsultantUserId { get; set; }

    [Required(ErrorMessage = "تاریخ شمسی جلسه الزامی است.")]
    public string JalaliDate { get; set; } = string.Empty;

    [Required(ErrorMessage = "ساعت جلسه الزامی است.")]
    public string TimeTehran { get; set; } = "17:00";

    [Required(ErrorMessage = "نام و نام خانوادگی الزامی است.")]
    [MaxLength(150)]
    public string VisitorFullName { get; set; } = string.Empty;

    [Required(ErrorMessage = "شماره موبایل الزامی است.")]
    public string VisitorMobile { get; set; } = string.Empty;

    [MaxLength(500)]
    public string? Notes { get; set; }
}

public sealed class LoginOtpViewModel
{
    [Required(ErrorMessage = "شماره موبایل الزامی است.")]
    public string MobileNumber { get; set; } = string.Empty;
    public string? ReturnUrl { get; set; }
    public string? ReferralCode { get; set; }
    public bool IsDevelopmentSmsMode { get; set; }
}

public sealed class RegisterStudentViewModel
{
    [Required(ErrorMessage = "شماره موبایل الزامی است.")]
    public string MobileNumber { get; set; } = string.Empty;

    [Required(ErrorMessage = "نام الزامی است.")]
    [MaxLength(100)]
    public string FirstName { get; set; } = string.Empty;

    [Required(ErrorMessage = "نام خانوادگی الزامی است.")]
    [MaxLength(100)]
    public string LastName { get; set; } = string.Empty;

    [Required(ErrorMessage = "انتخاب ساز الزامی است.")]
    public Guid InstrumentId { get; set; }

    public string? ReferralCode { get; set; }
    public string? ReturnUrl { get; set; }
    public IReadOnlyList<Instrument> Instruments { get; set; } = [];
}

public sealed class VerifyOtpViewModel
{
    [Required]
    public string MobileNumber { get; set; } = string.Empty;

    [Required(ErrorMessage = "کد تایید ۴ رقمی پیامک‌شده را وارد کنید.")]
    [StringLength(4, MinimumLength = 4, ErrorMessage = "کد تایید باید دقیقاً ۴ رقم باشد.")]
    public string OtpCode { get; set; } = string.Empty;

    public bool IsRegistration { get; set; }
    public string? ReferralCode { get; set; }
    public string? ReturnUrl { get; set; }
    public int CooldownSeconds { get; set; } = 60;
}

public sealed class CertificateVerificationViewModel
{
    public bool IsValid { get; init; }
    public string? CertificateNumber { get; init; }
    public string? StudentFullName { get; init; }
    public string? InstrumentName { get; init; }
    public string? CourseTitle { get; init; }
    public string? TeacherFullName { get; init; }
    public string? IssuedJalaliDate { get; init; }
}
