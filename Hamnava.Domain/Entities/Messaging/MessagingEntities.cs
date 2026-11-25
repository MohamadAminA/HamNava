using Hamnava.Domain.Entities.Identity;
using Hamnava.Domain.Enums;

namespace Hamnava.Domain.Entities.Messaging;

public class SmsTemplate : AuditableSoftDeleteEntity
{
    public string Code { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public SmsTriggerType TriggerType { get; set; }
    public string BodyTemplate { get; set; } = string.Empty;
    public string AllowedPlaceholdersCsv { get; set; } = string.Empty;
    public string? ProviderPatternCode { get; set; }
    public bool IsAutoTriggerEnabled { get; set; } = true;
    public bool IsActive { get; set; } = true;
}

public class SmsHistory : BaseEntity
{
    public string RecipientMobileNumber { get; set; } = string.Empty;
    public Guid? RecipientUserId { get; set; }
    public User? RecipientUser { get; set; }

    public Guid? SmsTemplateId { get; set; }
    public SmsTemplate? SmsTemplate { get; set; }

    public Guid? SmsCampaignId { get; set; }
    public SmsCampaign? SmsCampaign { get; set; }

    public SmsTriggerType TriggerType { get; set; }

    /// <summary>For OTP messages, store masked content (e.g. "کد تایید شما: ******") so codes are never exposed.</summary>
    public string ProtectedMessageContent { get; set; } = string.Empty;

    public string ProviderName { get; set; } = "ApiPayamakV3";
    public string? ProviderMessageId { get; set; }
    public string? ProviderRawResponse { get; set; }

    public SmsDeliveryStatus DeliveryStatus { get; set; } = SmsDeliveryStatus.Queued;
    public DateTime RequestedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? SubmittedAtUtc { get; set; }
    public DateTime? DeliveryReportedAtUtc { get; set; }

    public string? RelatedOperationType { get; set; }
    public Guid? RelatedEntityId { get; set; }
    public string? IdempotencyKey { get; set; }
    public Guid? InitiatedByUserId { get; set; }
}

public class SmsCampaign : AuditableSoftDeleteEntity
{
    public string Title { get; set; } = string.Empty;
    public string MessageBody { get; set; } = string.Empty;
    public Guid CreatedByTeacherOrAdminId { get; set; }
    public User CreatedByUser { get; set; } = null!;

    public DateOnly? TargetClassDateGregorian { get; set; }
    public int TotalRecipients { get; set; }
    public int SubmittedCount { get; set; }
    public int FailedCount { get; set; }

    public SmsCampaignStatus Status { get; set; } = SmsCampaignStatus.Draft;
    public string? HangfireJobId { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;

    public ICollection<SmsRecipient> Recipients { get; set; } = new List<SmsRecipient>();
}

public class SmsRecipient : BaseEntity
{
    public Guid SmsCampaignId { get; set; }
    public SmsCampaign SmsCampaign { get; set; } = null!;

    public Guid? UserId { get; set; }
    public User? User { get; set; }

    public string MobileNumber { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public SmsDeliveryStatus DeliveryStatus { get; set; } = SmsDeliveryStatus.Queued;
    public Guid? SmsHistoryId { get; set; }
    public string? ErrorMessage { get; set; }
}
