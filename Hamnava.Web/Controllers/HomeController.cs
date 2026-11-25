using System.Text;
using System.Xml.Linq;
using Hamnava.Core.DateTime;
using Hamnava.Core.Reports;
using Hamnava.DataLayer.Context;
using Hamnava.Domain.Constants;
using Hamnava.Domain.Entities.Cms;
using Hamnava.Domain.Enums;
using Hamnava.Web.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Hamnava.Web.Controllers;

public sealed class HomeController : Controller
{
    private readonly HamnavaDbContext _db;
    private readonly IJalaliDateService _jalali;
    private readonly IStoryImageService _storyService;
    private readonly IWebHostEnvironment _env;

    public HomeController(
        HamnavaDbContext db,
        IJalaliDateService jalali,
        IStoryImageService storyService,
        IWebHostEnvironment env)
    {
        _db = db;
        _jalali = jalali;
        _storyService = storyService;
        _env = env;
    }

    [HttpGet("/")]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var homePage = await _db.WebsitePages
            .AsNoTracking()
            .Include(p => p.Sections.Where(s => s.IsVisible))
            .FirstOrDefaultAsync(p => p.Slug == "home" && p.IsPublished, ct);

        var sections = homePage?.Sections.OrderBy(s => s.DisplayOrder).ToList() ?? [];

        var stats = await _db.HomepageStatistics
            .AsNoTracking()
            .Where(s => s.IsVisible)
            .OrderBy(s => s.DisplayOrder)
            .ToListAsync(ct);

        var features = await _db.HomepageFeatures
            .AsNoTracking()
            .Where(f => f.IsVisible)
            .OrderBy(f => f.DisplayOrder)
            .ToListAsync(ct);

        var instrumentsById = await _db.Instruments
            .AsNoTracking()
            .ToDictionaryAsync(i => i.Id, i => i, ct);

        var teacherRoleId = await _db.Roles.AsNoTracking()
            .Where(r => r.Name == RoleNames.Teacher)
            .Select(r => r.Id)
            .FirstOrDefaultAsync(ct);

        var activeTeachers = await _db.Users
            .AsNoTracking()
            .Include(u => u.TeacherInstruments).ThenInclude(ti => ti.Instrument)
            .Where(u => u.Status == UserStatus.Active && u.UserRoles.Any(ur => ur.RoleId == teacherRoleId))
            .OrderBy(u => u.CreatedAtUtc)
            .ToListAsync(ct);

        var teacherCards = activeTeachers.Select(u =>
        {
            var primaryInstName = u.PrimaryInstrumentId.HasValue && instrumentsById.TryGetValue(u.PrimaryInstrumentId.Value, out var pi)
                ? pi.Name
                : u.TeacherInstruments.FirstOrDefault()?.Instrument.Name;

            return new PublicTeacherCardViewModel(
                u.Id,
                u.FullName,
                u.PublicSlug ?? u.Id.ToString("N"),
                !string.IsNullOrWhiteSpace(primaryInstName) ? $"مدرس {primaryInstName}" : "مدرس موسیقی",
                u.Bio,
                string.IsNullOrWhiteSpace(u.ProfileImagePath) ? "/img/logo.webp" : u.ProfileImagePath);
        }).ToList();

        var consentedStudentIds = await _db.MediaConsents
            .AsNoTracking()
            .Where(c => c.AllowPublicPortfolio)
            .Select(c => c.StudentUserId)
            .ToListAsync(ct);

        var featuredStudentEntity = await _db.Users
            .AsNoTracking()
            .Where(u => u.Status == UserStatus.Active
                        && u.IsPublicProfileVisible
                        && !string.IsNullOrEmpty(u.PublicSlug)
                        && (consentedStudentIds.Count == 0 || consentedStudentIds.Contains(u.Id)))
            .OrderByDescending(u => u.CreatedAtUtc)
            .FirstOrDefaultAsync(ct);

        FeaturedStudentShowcaseViewModel? featuredStudent = null;
        if (featuredStudentEntity is not null)
        {
            var firstEnrollmentDate = await _db.Enrollments
                .AsNoTracking()
                .Where(e => e.StudentUserId == featuredStudentEntity.Id)
                .OrderBy(e => e.StartDateGregorian)
                .Select(e => (DateOnly?)e.StartDateGregorian)
                .FirstOrDefaultAsync(ct);

            var durationText = CalculateLearningDurationFa(firstEnrollmentDate, _jalali.TehranToday);

            var featuredMedia = await _db.PortfolioMedias
                .AsNoTracking()
                .Where(m => m.StudentUserId == featuredStudentEntity.Id
                            && m.IsPubliclyVisible
                            && m.ProcessingStatus == MediaProcessingStatus.Completed)
                .OrderByDescending(m => m.MediaType == MediaType.Video)
                .ThenByDescending(m => m.CreatedAtUtc)
                .FirstOrDefaultAsync(ct);

            var instBadge = featuredStudentEntity.PrimaryInstrumentId.HasValue
                            && instrumentsById.TryGetValue(featuredStudentEntity.PrimaryInstrumentId.Value, out var inst)
                ? inst.Name
                : "موسیقی";

            featuredStudent = new FeaturedStudentShowcaseViewModel(
                featuredStudentEntity.Id,
                featuredStudentEntity.FullName,
                featuredStudentEntity.PublicSlug!,
                instBadge,
                durationText,
                featuredMedia?.ThumbnailStorageKey is not null ? $"/media/{featuredMedia.Id}/thumb" : "/img/instruments/piano-removebg-preview.png",
                featuredMedia is not null ? $"/media/{featuredMedia.Id}" : null);
        }

        var studentCategoryThumbs = await _db.Instruments
            .AsNoTracking()
            .Where(i => i.IsActive)
            .OrderBy(i => i.DisplayOrder)
            .Take(3)
            .Select(i => new StudentCategoryThumbViewModel(
                i.Name,
                string.IsNullOrWhiteSpace(i.ImagePath) ? "/img/instruments/guitar-removebg-preview.png" : i.ImagePath,
                $"/students?instrument={i.Slug}"))
            .ToListAsync(ct);

        var websiteAssets = await _db.WebsiteAssets
            .AsNoTracking()
            .ToListAsync(ct);

        var worldTabs = BuildWorldTabs(websiteAssets);

        var articles = await _db.PublicArticles
            .AsNoTracking()
            .Where(a => a.IsPublished)
            .OrderByDescending(a => a.PublishedAtUtc ?? a.CreatedAtUtc)
            .Take(4)
            .ToListAsync(ct);

        var events = await _db.PublicEvents
            .AsNoTracking()
            .Where(e => e.IsPublished)
            .OrderByDescending(e => e.EventDateGregorian)
            .Take(4)
            .ToListAsync(ct);

        var contact = await _db.ContactInformations
            .AsNoTracking()
            .FirstOrDefaultAsync(ct) ?? new ContactInformation();

        var vm = new HomePageViewModel
        {
            PageMeta = homePage,
            HeroSection = sections.FirstOrDefault(s => s.SectionKey == "hero"),
            AboutSection = sections.FirstOrDefault(s => s.SectionKey == "about"),
            CtaSection = sections.FirstOrDefault(s => s.SectionKey == "cta"),
            Statistics = stats,
            WhyFeatures = features,
            WorldTabs = worldTabs,
            Teachers = teacherCards,
            FeaturedStudent = featuredStudent,
            StudentCategoryThumbs = studentCategoryThumbs,
            Articles = articles,
            Events = events,
            Contact = contact
        };

        ViewData["Title"] = homePage?.MetaTitle ?? "آکادمی موسیقی هم‌نوا | آموزش تخصصی موسیقی در اصفهان";
        ViewData["MetaDescription"] = homePage?.MetaDescription ?? "آموزشگاه موسیقی هم‌نوا با اساتید مجرب، محیطی آرام و مدرن برای یادگیری تخصصی سازها.";

        return View(vm);
    }

    [HttpGet("/about")]
    public async Task<IActionResult> About(CancellationToken ct)
    {
        var page = await _db.WebsitePages
            .AsNoTracking()
            .Include(p => p.Sections.Where(s => s.IsVisible))
            .FirstOrDefaultAsync(p => p.Slug == "about" && p.IsPublished, ct);

        var contact = await _db.ContactInformations.AsNoTracking().FirstOrDefaultAsync(ct) ?? new ContactInformation();
        ViewData["Title"] = page?.MetaTitle ?? "درباره ما | آکادمی موسیقی هم‌نوا";
        ViewData["Contact"] = contact;
        return View(page);
    }

    [HttpGet("/teachers")]
    public async Task<IActionResult> Teachers(string? instrument, CancellationToken ct)
    {
        var teacherRoleId = await _db.Roles.AsNoTracking()
            .Where(r => r.Name == RoleNames.Teacher)
            .Select(r => r.Id)
            .FirstOrDefaultAsync(ct);

        var instruments = await _db.Instruments.AsNoTracking()
            .Where(i => i.IsActive)
            .OrderBy(i => i.DisplayOrder)
            .ToListAsync(ct);

        var instById = instruments.ToDictionary(i => i.Id, i => i);
        Guid? selectedInstrumentId = !string.IsNullOrWhiteSpace(instrument)
            ? instruments.FirstOrDefault(i => i.Slug.Equals(instrument, StringComparison.OrdinalIgnoreCase))?.Id
            : null;

        var query = _db.Users
            .AsNoTracking()
            .Include(u => u.TeacherInstruments).ThenInclude(ti => ti.Instrument)
            .Where(u => u.Status == UserStatus.Active && u.UserRoles.Any(ur => ur.RoleId == teacherRoleId));

        if (selectedInstrumentId.HasValue)
        {
            var idVal = selectedInstrumentId.Value;
            query = query.Where(u => u.PrimaryInstrumentId == idVal || u.TeacherInstruments.Any(ti => ti.InstrumentId == idVal));
        }

        var teacherEntities = await query.OrderBy(u => u.CreatedAtUtc).ToListAsync(ct);
        var teachers = teacherEntities.Select(u =>
        {
            var instName = u.PrimaryInstrumentId.HasValue && instById.TryGetValue(u.PrimaryInstrumentId.Value, out var pi)
                ? pi.Name
                : u.TeacherInstruments.FirstOrDefault()?.Instrument.Name;

            return new PublicTeacherCardViewModel(
                u.Id,
                u.FullName,
                u.PublicSlug ?? u.Id.ToString("N"),
                !string.IsNullOrWhiteSpace(instName) ? $"مدرس {instName}" : "مدرس موسیقی",
                u.Bio,
                string.IsNullOrWhiteSpace(u.ProfileImagePath) ? "/img/logo.webp" : u.ProfileImagePath);
        }).ToList();

        ViewData["Title"] = "اساتید مجموعه | آکادمی موسیقی هم‌نوا";
        ViewData["Instruments"] = instruments;
        ViewData["SelectedInstrument"] = instrument;
        return View(teachers);
    }

    [HttpGet("/teachers/{slug}")]
    public async Task<IActionResult> TeacherProfile(string slug, CancellationToken ct)
    {
        var teacher = await _db.Users
            .AsNoTracking()
            .Include(u => u.TeacherInstruments).ThenInclude(ti => ti.Instrument)
            .FirstOrDefaultAsync(u => u.Status == UserStatus.Active && u.PublicSlug == slug, ct);

        if (teacher is null)
        {
            return NotFound();
        }

        string? instName = null;
        if (teacher.PrimaryInstrumentId.HasValue)
        {
            instName = await _db.Instruments.AsNoTracking()
                .Where(i => i.Id == teacher.PrimaryInstrumentId.Value)
                .Select(i => i.Name)
                .FirstOrDefaultAsync(ct);
        }
        instName ??= teacher.TeacherInstruments.FirstOrDefault()?.Instrument.Name;

        var vm = new PublicTeacherCardViewModel(
            teacher.Id,
            teacher.FullName,
            teacher.PublicSlug!,
            !string.IsNullOrWhiteSpace(instName) ? $"مدرس {instName}" : "مدرس موسیقی",
            teacher.Bio,
            string.IsNullOrWhiteSpace(teacher.ProfileImagePath) ? "/img/logo.webp" : teacher.ProfileImagePath);

        ViewData["Title"] = $"{teacher.FullName} | اساتید آکادمی موسیقی هم‌نوا";
        return View(vm);
    }

    [HttpGet("/students")]
    public async Task<IActionResult> Students(string? instrument, CancellationToken ct)
    {
        var instruments = await _db.Instruments.AsNoTracking()
            .Where(i => i.IsActive)
            .OrderBy(i => i.DisplayOrder)
            .ToListAsync(ct);

        var instById = instruments.ToDictionary(i => i.Id, i => i);
        Guid? selectedInstrumentId = !string.IsNullOrWhiteSpace(instrument)
            ? instruments.FirstOrDefault(i => i.Slug.Equals(instrument, StringComparison.OrdinalIgnoreCase))?.Id
            : null;

        var query = _db.Users
            .AsNoTracking()
            .Where(u => u.Status == UserStatus.Active
                        && u.IsPublicProfileVisible
                        && !string.IsNullOrEmpty(u.PublicSlug));

        if (selectedInstrumentId.HasValue)
        {
            var idVal = selectedInstrumentId.Value;
            query = query.Where(u => u.PrimaryInstrumentId == idVal);
        }

        var students = await query.OrderByDescending(u => u.CreatedAtUtc).ToListAsync(ct);
        var items = new List<FeaturedStudentShowcaseViewModel>(students.Count);

        foreach (var student in students)
        {
            var firstStart = await _db.Enrollments.AsNoTracking()
                .Where(e => e.StudentUserId == student.Id)
                .OrderBy(e => e.StartDateGregorian)
                .Select(e => (DateOnly?)e.StartDateGregorian)
                .FirstOrDefaultAsync(ct);

            var media = await _db.PortfolioMedias.AsNoTracking()
                .Where(m => m.StudentUserId == student.Id && m.IsPubliclyVisible && m.ProcessingStatus == MediaProcessingStatus.Completed)
                .OrderByDescending(m => m.CreatedAtUtc)
                .FirstOrDefaultAsync(ct);

            var instBadge = student.PrimaryInstrumentId.HasValue && instById.TryGetValue(student.PrimaryInstrumentId.Value, out var pi)
                ? pi.Name
                : "موسیقی";

            items.Add(new FeaturedStudentShowcaseViewModel(
                student.Id,
                student.FullName,
                student.PublicSlug!,
                instBadge,
                CalculateLearningDurationFa(firstStart, _jalali.TehranToday),
                media?.ThumbnailStorageKey is not null ? $"/media/{media.Id}/thumb" : "/img/logo.webp",
                media is not null ? $"/media/{media.Id}" : null));
        }

        ViewData["Title"] = "هنرجویان هم‌نوا | پورتفولیو و اجراها";
        ViewData["Instruments"] = instruments;
        ViewData["SelectedInstrument"] = instrument;
        return View(items);
    }

    [HttpGet("/student/{englishSlug}")]
    public async Task<IActionResult> StudentPortfolio(string englishSlug, CancellationToken ct)
    {
        var student = await _db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u =>
                u.PublicSlug == englishSlug
                && u.Status == UserStatus.Active
                && u.IsPublicProfileVisible, ct);

        if (student is null)
        {
            return NotFound();
        }

        var consent = await _db.MediaConsents
            .AsNoTracking()
            .OrderByDescending(c => c.ConsentedAtUtc)
            .FirstOrDefaultAsync(c => c.StudentUserId == student.Id, ct);

        if (consent is not null && !consent.AllowPublicPortfolio)
        {
            return NotFound();
        }

        var firstEnrollment = await _db.Enrollments
            .AsNoTracking()
            .Include(e => e.Teacher)
            .Include(e => e.Instrument)
            .Where(e => e.StudentUserId == student.Id)
            .OrderBy(e => e.StartDateGregorian)
            .FirstOrDefaultAsync(ct);

        var instrumentName = firstEnrollment?.Instrument.Name;
        if (string.IsNullOrWhiteSpace(instrumentName) && student.PrimaryInstrumentId.HasValue)
        {
            instrumentName = await _db.Instruments.AsNoTracking()
                .Where(i => i.Id == student.PrimaryInstrumentId.Value)
                .Select(i => i.Name)
                .FirstOrDefaultAsync(ct);
        }

        var publicMedia = await _db.PortfolioMedias
            .AsNoTracking()
            .Where(m => m.StudentUserId == student.Id
                        && m.IsPubliclyVisible
                        && m.ProcessingStatus == MediaProcessingStatus.Completed)
            .OrderByDescending(m => m.CreatedAtUtc)
            .ToListAsync(ct);

        var vm = new PublicStudentPortfolioViewModel
        {
            StudentUserId = student.Id,
            FullName = student.FullName,
            EnglishFullName = student.EnglishFullName,
            EnglishSlug = student.PublicSlug!,
            ProfileImageUrl = string.IsNullOrWhiteSpace(student.ProfileImagePath) ? "/img/logo.webp" : student.ProfileImagePath,
            InstrumentName = instrumentName ?? "موسیقی",
            TeacherName = firstEnrollment?.Teacher.FullName ?? "اساتید آکادمی هم‌نوا",
            CalculatedLearningDurationFa = CalculateLearningDurationFa(firstEnrollment?.StartDateGregorian, _jalali.TehranToday),
            Videos = publicMedia.Where(m => m.MediaType == MediaType.Video)
                .Select(m => new PortfolioMediaItemViewModel(
                    m.Id,
                    m.Title,
                    m.Description,
                    $"/media/{m.Id}",
                    m.ThumbnailStorageKey is not null ? $"/media/{m.Id}/thumb" : null,
                    _jalali.FormatDate(m.CreatedAtUtc),
                    m.AllowDownload))
                .ToList(),
            Images = publicMedia.Where(m => m.MediaType == MediaType.Image)
                .Select(m => new PortfolioMediaItemViewModel(
                    m.Id,
                    m.Title,
                    m.Description,
                    $"/media/{m.Id}",
                    m.ThumbnailStorageKey is not null ? $"/media/{m.Id}/thumb" : null,
                    _jalali.FormatDate(m.CreatedAtUtc),
                    m.AllowDownload))
                .ToList()
        };

        ViewData["Title"] = $"پورتفولیو هنری {student.FullName} | آکادمی موسیقی هم‌نوا";
        ViewData["MetaDescription"] = $"مشاهده اجراها و پیشرفت هنری {student.FullName} در ساز {vm.InstrumentName} در آکادمی موسیقی هم‌نوا.";
        return View(vm);
    }

    [HttpGet("/student/{englishSlug}/story.png")]
    public async Task<IActionResult> DownloadStudentStory(string englishSlug, CancellationToken ct)
    {
        var baseUrl = $"{Request.Scheme}://{Request.Host}";
        var fontPath = Path.Combine(_env.WebRootPath, "fonts", "vazirmatn", "Vazirmatn-RD-Bold.woff2");
        var result = await _storyService.GenerateStudentInstagramStoryAsync(englishSlug, baseUrl, fontPath, ct);
        if (!result.Succeeded || result.Data is null)
        {
            return NotFound(result.Message);
        }

        return File(result.Data, "image/png", $"hamnava-story-{englishSlug}.png");
    }

    [HttpGet("/pricing")]
    public async Task<IActionResult> Pricing(CancellationToken ct)
    {
        var plans = await _db.CoursePlans
            .AsNoTracking()
            .Where(p => p.IsActive)
            .OrderBy(p => p.DisplayOrder)
            .ToListAsync(ct);

        var rentable = await _db.Instruments
            .AsNoTracking()
            .Where(i => i.IsActive && i.IsRentable)
            .OrderBy(i => i.DisplayOrder)
            .ToListAsync(ct);

        ViewData["Title"] = "شهریه دوره‌ها و اجاره ساز | آکادمی موسیقی هم‌نوا";
        return View(new PricingPageViewModel
        {
            Plans = plans,
            RentableInstruments = rentable
        });
    }

    [HttpGet("/blog")]
    public async Task<IActionResult> Articles(CancellationToken ct)
    {
        var articles = await _db.PublicArticles
            .AsNoTracking()
            .Where(a => a.IsPublished)
            .OrderByDescending(a => a.PublishedAtUtc ?? a.CreatedAtUtc)
            .ToListAsync(ct);

        ViewData["Title"] = "وبلاگ آموزشی | آکادمی موسیقی هم‌نوا";
        return View(articles);
    }

    [HttpGet("/blog/{slug}")]
    public async Task<IActionResult> ArticleDetail(string slug, CancellationToken ct)
    {
        var article = await _db.PublicArticles
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Slug == slug && a.IsPublished, ct);

        if (article is null)
        {
            return NotFound();
        }

        ViewData["Title"] = $"{article.Title} | وبلاگ هم‌نوا";
        ViewData["MetaDescription"] = article.Summary;
        return View(article);
    }

    [HttpGet("/contact")]
    public async Task<IActionResult> Contact(CancellationToken ct)
    {
        var contact = await _db.ContactInformations.AsNoTracking().FirstOrDefaultAsync(ct) ?? new ContactInformation();
        ViewData["Title"] = "تماس با ما | آکادمی موسیقی هم‌نوا";
        return View(contact);
    }

    [HttpGet]
    public IActionResult RobotsTxt()
    {
        var baseUrl = $"{Request.Scheme}://{Request.Host}";
        var content = new StringBuilder()
            .AppendLine("User-agent: *")
            .AppendLine("Allow: /")
            .AppendLine("Disallow: /dashboard/")
            .AppendLine("Disallow: /account/")
            .AppendLine("Disallow: /payments/")
            .AppendLine($"Sitemap: {baseUrl}/sitemap.xml")
            .ToString();

        return Content(content, "text/plain", Encoding.UTF8);
    }

    [HttpGet]
    public async Task<IActionResult> SitemapXml(CancellationToken ct)
    {
        var baseUrl = $"{Request.Scheme}://{Request.Host}";
        XNamespace ns = "http://www.sitemaps.org/schemas/sitemap/0.9";
        var urls = new List<XElement>
        {
            BuildUrl(ns, $"{baseUrl}/", "weekly", "1.0"),
            BuildUrl(ns, $"{baseUrl}/about", "monthly", "0.8"),
            BuildUrl(ns, $"{baseUrl}/teachers", "weekly", "0.9"),
            BuildUrl(ns, $"{baseUrl}/students", "weekly", "0.8"),
            BuildUrl(ns, $"{baseUrl}/pricing", "weekly", "0.9"),
            BuildUrl(ns, $"{baseUrl}/consultation", "weekly", "0.9"),
            BuildUrl(ns, $"{baseUrl}/blog", "weekly", "0.8"),
            BuildUrl(ns, $"{baseUrl}/contact", "monthly", "0.7")
        };

        var publicSlugs = await _db.Users.AsNoTracking()
            .Where(u => u.Status == UserStatus.Active && u.IsPublicProfileVisible && !string.IsNullOrEmpty(u.PublicSlug))
            .Select(u => u.PublicSlug!)
            .ToListAsync(ct);

        foreach (var slug in publicSlugs)
        {
            urls.Add(BuildUrl(ns, $"{baseUrl}/student/{slug}", "weekly", "0.7"));
        }

        var articleSlugs = await _db.PublicArticles.AsNoTracking()
            .Where(a => a.IsPublished)
            .Select(a => a.Slug)
            .ToListAsync(ct);

        foreach (var slug in articleSlugs)
        {
            urls.Add(BuildUrl(ns, $"{baseUrl}/blog/{slug}", "monthly", "0.7"));
        }

        var doc = new XDocument(new XElement(ns + "urlset", urls));
        return Content(doc.ToString(), "application/xml", Encoding.UTF8);
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        ViewData["Title"] = "خطای سیستم | هم‌نوا";
        return View();
    }

    private static XElement BuildUrl(XNamespace ns, string loc, string changeFreq, string priority) =>
        new(ns + "url",
            new XElement(ns + "loc", loc),
            new XElement(ns + "changefreq", changeFreq),
            new XElement(ns + "priority", priority));

    private static List<WorldGalleryTabViewModel> BuildWorldTabs(IReadOnlyList<WebsiteAsset> assets)
    {
        string PickImage(string key, string fallback) =>
            assets.FirstOrDefault(g => g.AssetKey.Equals(key, StringComparison.OrdinalIgnoreCase))?.FilePath ?? fallback;

        return
        [
            new("teachers", "تدریس مدرسین", PickImage("world-teachers", "/img/teachers/mohammad amin aghakabiri.png"), "تدریس خصوصی و تخصصی اساتید هم‌نوا", "/teachers"),
            new("students", "نوازندگی هنرجویان", PickImage("world-students", "/img/instruments/piano-removebg-preview.png"), "اجراها و پیشرفت هنرجویان مجموعه", "/students"),
            new("space", "فضای مجموعه", PickImage("world-space", "/img/instruments/guitar-removebg-preview.png"), "محیطی آرام، آکوستیک و استاندارد", "/about"),
            new("events", "رویدادها و اجراها", PickImage("world-events", "/img/instruments/violin-removebg-preview.png"), "کنسرت‌های هنرجویی و همنوازی‌ها", "/blog")
        ];
    }

    private string CalculateLearningDurationFa(DateOnly? firstEnrollmentDate, DateOnly today)
    {
        if (!firstEnrollmentDate.HasValue)
        {
            return "هنرجوی فعال هم‌نوا";
        }

        var totalDays = Math.Max(1, today.DayNumber - firstEnrollmentDate.Value.DayNumber);
        var months = totalDays / 30;
        if (months < 1)
        {
            return "تازه‌وارد (کمتر از ۱ ماه سابقه آموزش)";
        }

        if (months < 12)
        {
            return $"{_jalali.ToPersianDigits(months.ToString())} ماه سابقه آموزش در هم‌نوا";
        }

        var years = months / 12;
        var remMonths = months % 12;
        return remMonths == 0
            ? $"{_jalali.ToPersianDigits(years.ToString())} سال سابقه آموزش در هم‌نوا"
            : $"{_jalali.ToPersianDigits(years.ToString())} سال و {_jalali.ToPersianDigits(remMonths.ToString())} ماه سابقه آموزش";
    }
}
