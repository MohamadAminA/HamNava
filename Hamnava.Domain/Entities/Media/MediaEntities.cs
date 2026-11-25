using Hamnava.Domain.Entities.Identity;
using Hamnava.Domain.Enums;

namespace Hamnava.Domain.Entities.Media;

public class PortfolioMedia : AuditableSoftDeleteEntity
{
    public Guid StudentUserId { get; set; }
    public User Student { get; set; } = null!;

    public Guid UploadedByUserId { get; set; }
    public User UploadedByUser { get; set; } = null!;

    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public MediaType MediaType { get; set; }
    public MediaProcessingStatus ProcessingStatus { get; set; } = MediaProcessingStatus.Pending;

    // Storage metadata (never store large binary blobs in SQL Server; never expose raw disk paths to clients)
    public string OriginalStorageKey { get; set; } = string.Empty;
    public string? OptimizedStorageKey { get; set; }
    public string? ThumbnailStorageKey { get; set; }

    public string OriginalFileNameSanitized { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long OriginalFileSizeBytes { get; set; }
    public long? OptimizedFileSizeBytes { get; set; }
    public int? WidthPx { get; set; }
    public int? HeightPx { get; set; }
    public double? DurationSeconds { get; set; }

    public bool IsPubliclyVisible { get; set; }
    public bool AllowDownload { get; set; } = true;
    public int DisplayOrder { get; set; }
    public bool IsFeaturedOnHomepage { get; set; }

    public ICollection<MediaProcessingJob> ProcessingJobs { get; set; } = new List<MediaProcessingJob>();
}

public class MediaProcessingJob : BaseEntity
{
    public Guid PortfolioMediaId { get; set; }
    public PortfolioMedia PortfolioMedia { get; set; } = null!;

    public string? HangfireJobId { get; set; }
    public MediaProcessingStatus Status { get; set; } = MediaProcessingStatus.Pending;
    public int ProgressPercentage { get; set; }
    public int AttemptCount { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime? StartedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
}

public class MediaConsent : BaseEntity
{
    public Guid StudentUserId { get; set; }
    public User Student { get; set; } = null!;

    public bool AllowPublicPortfolio { get; set; }
    public bool AllowPublicVideos { get; set; }
    public bool AllowPublicImages { get; set; }
    public bool AllowHomepageShowcase { get; set; }
    public string? GuardianNameIfMinor { get; set; }
    public DateTime ConsentedAtUtc { get; set; } = DateTime.UtcNow;
    public string? ConsentIpAddress { get; set; }
}

public class PortfolioView : BaseEntity
{
    public Guid StudentUserId { get; set; }
    public User Student { get; set; } = null!;

    public string? ViewerIpHash { get; set; }
    public string? ReferrerHost { get; set; }
    public DateTime ViewedAtUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Admin-managed 9:16 Instagram Story composition template stored in SQL Server.
/// </summary>
public class StoryTemplate : AuditableSoftDeleteEntity
{
    public string Name { get; set; } = string.Empty;
    public string? BackgroundImageStorageKey { get; set; }
    public string PrimaryColorHex { get; set; } = "#6FA7A8";
    public string SecondaryColorHex { get; set; } = "#2A6E6F";
    public string AccentColorHex { get; set; } = "#C8B3A0";
    public string TextColorHex { get; set; } = "#FFFFFF";
    public string LayoutConfigJson { get; set; } = "{}";
    public bool IsDefault { get; set; }
    public bool IsActive { get; set; } = true;
}
