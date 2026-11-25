namespace Hamnava.Domain.Entities.Cms;

public class SystemSetting : BaseEntity
{
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public string Category { get; set; } = "General";
    public string DataType { get; set; } = "string";
    public string DisplayLabelFa { get; set; } = string.Empty;
    public string? DescriptionFa { get; set; }
    public bool IsEditableInAdminPanel { get; set; } = true;
}

public class WebsitePage : AuditableSoftDeleteEntity
{
    public string Slug { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string MetaTitle { get; set; } = string.Empty;
    public string MetaDescription { get; set; } = string.Empty;
    public string? CanonicalPath { get; set; }
    public string? OgImagePath { get; set; }
    public bool IsPublished { get; set; } = true;

    public ICollection<WebsiteSection> Sections { get; set; } = new List<WebsiteSection>();
}

public class WebsiteSection : AuditableSoftDeleteEntity
{
    public Guid WebsitePageId { get; set; }
    public WebsitePage WebsitePage { get; set; } = null!;

    public string SectionKey { get; set; } = string.Empty;
    public string? EyebrowLabel { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Subtitle { get; set; }
    public string? BodyHtml { get; set; }
    public string? PrimaryCtaText { get; set; }
    public string? PrimaryCtaUrl { get; set; }
    public string? SecondaryCtaText { get; set; }
    public string? SecondaryCtaUrl { get; set; }
    public string? ImagePath { get; set; }
    public int DisplayOrder { get; set; }
    public bool IsVisible { get; set; } = true;
}

public class WebsiteMenu : AuditableSoftDeleteEntity
{
    public string MenuLocation { get; set; } = "Header";
    public string Title { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public Guid? ParentMenuId { get; set; }
    public int DisplayOrder { get; set; }
    public bool IsVisible { get; set; } = true;
    public bool OpenInNewTab { get; set; }
}

public class WebsiteAsset : AuditableSoftDeleteEntity
{
    public string AssetKey { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public string AltText { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
}

public class HomepageStatistic : AuditableSoftDeleteEntity
{
    public string Label { get; set; } = string.Empty;
    public string DisplayValue { get; set; } = string.Empty;
    public string? Suffix { get; set; }
    public string? IconKey { get; set; }
    public int DisplayOrder { get; set; }
    public bool IsVisible { get; set; } = true;
}

public class HomepageFeature : AuditableSoftDeleteEntity
{
    public string SectionGroup { get; set; } = "WhyHamnava";
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string? IconKey { get; set; }
    public int DisplayOrder { get; set; }
    public bool IsVisible { get; set; } = true;
}

public class ContactInformation : BaseEntity
{
    public string AcademyName { get; set; } = "آموزشگاه موسیقی هم‌نوا";
    public string PhonePrimary { get; set; } = string.Empty;
    public string? PhoneSecondary { get; set; }
    public string? Email { get; set; }
    public string Address { get; set; } = string.Empty;
    public string WorkingHours { get; set; } = string.Empty;
    public string? MapEmbedOrCoordinates { get; set; }
    public string? InstagramUrl { get; set; }
    public string? TelegramUrl { get; set; }
    public string? WhatsAppUrl { get; set; }
    public string? AparatOrYoutubeUrl { get; set; }
    public string? FooterAboutText { get; set; }
    public string? CopyrightText { get; set; }
}

public class PublicArticle : AuditableSoftDeleteEntity
{
    public string Slug { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string ContentHtml { get; set; } = string.Empty;
    public string? CoverImagePath { get; set; }
    public string? AuthorDisplayName { get; set; }
    public string? MetaDescription { get; set; }
    public DateTime? PublishedAtUtc { get; set; }
    public bool IsPublished { get; set; }
    public int DisplayOrder { get; set; }
}

public class PublicEvent : AuditableSoftDeleteEntity
{
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string? ImagePath { get; set; }
    public DateOnly EventDateGregorian { get; set; }
    public string? Location { get; set; }
    public bool IsPublished { get; set; } = true;
    public int DisplayOrder { get; set; }
}

public class PublicTestimonial : AuditableSoftDeleteEntity
{
    public string AuthorName { get; set; } = string.Empty;
    public string AuthorRole { get; set; } = string.Empty;
    public string Quote { get; set; } = string.Empty;
    public int Rating { get; set; } = 5;
    public string? AvatarPath { get; set; }
    public bool IsPublished { get; set; } = true;
    public int DisplayOrder { get; set; }
}

public class FaqItem : AuditableSoftDeleteEntity
{
    public string Question { get; set; } = string.Empty;
    public string Answer { get; set; } = string.Empty;
    public int DisplayOrder { get; set; }
    public bool IsVisible { get; set; } = true;
}
