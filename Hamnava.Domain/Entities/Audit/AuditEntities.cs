using Hamnava.Domain.Entities.Identity;

namespace Hamnava.Domain.Entities.Audit;

public class AuditLog : BaseEntity
{
    public Guid? ActorUserId { get; set; }
    public string? ActorRole { get; set; }
    public string Action { get; set; } = string.Empty;
    public string EntityName { get; set; } = string.Empty;
    public string? EntityId { get; set; }
    public string? BeforeStateJson { get; set; }
    public string? AfterStateJson { get; set; }
    public string? CorrelationId { get; set; }
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public DateTime OccurredAtUtc { get; set; } = DateTime.UtcNow;
}

public class IdempotencyRecord : BaseEntity
{
    public string Key { get; set; } = string.Empty;
    public string Scope { get; set; } = string.Empty;
    public string RequestHash { get; set; } = string.Empty;
    public string? ResultSnapshotJson { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
}

public class BackgroundJobRecord : BaseEntity
{
    public string HangfireJobId { get; set; } = string.Empty;
    public string JobType { get; set; } = string.Empty;
    public string? RelatedEntityId { get; set; }
    public string Status { get; set; } = "Scheduled";
    public string? ErrorDetails { get; set; }
    public DateTime? ExecutedAtUtc { get; set; }
}

public class SystemNotification : BaseEntity
{
    public Guid RecipientUserId { get; set; }
    public User RecipientUser { get; set; } = null!;

    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string? ActionUrl { get; set; }
    public bool IsRead { get; set; }
    public DateTime? ReadAtUtc { get; set; }
}
