using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Hamnava.Core.Common;
using Hamnava.Core.DateTime;
using Hamnava.DataLayer.Context;
using Hamnava.Domain.Entities.Messaging;
using Hamnava.Domain.Enums;
using Hamnava.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Hamnava.Core.Messaging;

public sealed class SmsProviderOptions
{
    public const string SectionName = "SmsProvider:ApiPayamak";

    /// <summary>
    /// Supported modes:
    /// - "Development": Safe local dev-only provider (never sends external SMS; never logs OTP in Production).
    /// - "ApiPayamakV3": REST API v3 for api-payamak.com (https://api-payamak.com/help/restV3.php).
    /// </summary>
    public string Mode { get; set; } = "Development";
    public string BaseUrl { get; set; } = "https://api-payamak.com/api/v3/rest/";
    public string SendEndpointPath { get; set; } = "sms/send";
    public string PatternSendEndpointPath { get; set; } = "sms/pattern-send";
    public string StatusEndpointPath { get; set; } = "sms/status";
    public string ApiToken { get; set; } = string.Empty;
    public string SenderNumber { get; set; } = string.Empty;
    public int MessageType { get; set; } = 0;
    public int TimeoutSeconds { get; set; } = 15;
}

public sealed record SmsMessageDto(
    string RecipientMobile,
    string Body,
    bool IsOtp = false,
    string? PatternCode = null,
    IReadOnlyDictionary<string, string>? TemplateParameters = null);

public sealed record SmsSendResult(
    bool AcceptedByProvider,
    SmsDeliveryStatus Status,
    string? ProviderMessageId,
    string? RawResponse,
    string? ErrorMessage);

public sealed record SmsDeliveryReport(
    string ProviderMessageId,
    SmsDeliveryStatus Status,
    System.DateTime CheckedAtUtc,
    string? RawResponse);

public interface ISmsProvider
{
    string ProviderName { get; }
    Task<SmsSendResult> SendAsync(SmsMessageDto message, CancellationToken ct = default);
    Task<SmsDeliveryReport> GetDeliveryReportAsync(string providerMessageId, CancellationToken ct = default);
}

/// <summary>
/// ApiPayamak REST API v3 provider implementation (https://api-payamak.com/api/v3/rest/).
/// Supports standard sending (sms/send), pattern sending (sms/pattern-send), and status tracking (sms/status).
/// </summary>
public sealed class ApiPayamakSmsProvider : ISmsProvider
{
    private readonly HttpClient _httpClient;
    private readonly SmsProviderOptions _options;
    private readonly ILogger<ApiPayamakSmsProvider> _logger;

    public string ProviderName => "ApiPayamakV3";

    public ApiPayamakSmsProvider(
        HttpClient httpClient,
        IOptions<SmsProviderOptions> options,
        ILogger<ApiPayamakSmsProvider> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
        _httpClient.Timeout = TimeSpan.FromSeconds(Math.Clamp(_options.TimeoutSeconds, 5, 60));
    }

    public async Task<SmsSendResult> SendAsync(SmsMessageDto message, CancellationToken ct = default)
    {
        if (string.Equals(_options.Mode, "Development", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogInformation("[DEV-SMS] Simulated SMS accepted for {Mobile} (IsOtp={IsOtp})",
                MobileNumber.TryParse(message.RecipientMobile, out var m) ? m!.ToMasked() : "UNKNOWN",
                message.IsOtp);

            return new SmsSendResult(
                true,
                SmsDeliveryStatus.AcceptedByProvider,
                $"DEV-{Guid.NewGuid():N}",
                "Simulated in Development mode",
                null);
        }

        if (!string.Equals(_options.Mode, "ApiPayamakV3", StringComparison.OrdinalIgnoreCase))
        {
            return new SmsSendResult(false, SmsDeliveryStatus.Failed, null, null, "حالت پیکربندی پنل پیامک معتبر نیست.");
        }

        if (string.IsNullOrWhiteSpace(_options.BaseUrl) || string.IsNullOrWhiteSpace(_options.ApiToken))
        {
            return new SmsSendResult(false, SmsDeliveryStatus.Failed, null, null, "آدرس یا توکن وب‌سرویس پیامک تنظیم نشده است.");
        }

        if (string.IsNullOrWhiteSpace(_options.SenderNumber))
        {
            return new SmsSendResult(false, SmsDeliveryStatus.Failed, null, null, "شماره خط فرستنده (SenderNumber) در تنظیمات SmsProvider:ApiPayamak وارد نشده است.");
        }

        try
        {
            var patternId = 0;
            var usePattern = !string.IsNullOrWhiteSpace(message.PatternCode)
                             && int.TryParse(message.PatternCode, out patternId)
                             && message.TemplateParameters is { Count: > 0 };

            var endpoint = BuildEndpoint(usePattern ? _options.PatternSendEndpointPath : _options.SendEndpointPath);
            object payload = usePattern
                ? new
                {
                    from = _options.SenderNumber,
                    recipients = new[] { message.RecipientMobile },
                    message = message.TemplateParameters,
                    pattern_id = patternId,
                    type = _options.MessageType
                }
                : new
                {
                    from = _options.SenderNumber,
                    recipients = new[] { message.RecipientMobile },
                    message = message.Body,
                    type = _options.MessageType
                };

            using var req = CreateAuthorizedJsonPost(endpoint, payload);
            using var response = await _httpClient.SendAsync(req, ct);
            var raw = await response.Content.ReadAsStringAsync(ct);

            return ParseSendResponse(response.IsSuccessStatusCode, (int)response.StatusCode, raw);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ApiPayamak v3 SMS send request failed");
            return new SmsSendResult(false, SmsDeliveryStatus.Failed, null, null, "خطای شبکه هنگام ارسال پیامک.");
        }
    }

    public async Task<SmsDeliveryReport> GetDeliveryReportAsync(string providerMessageId, CancellationToken ct = default)
    {
        if (string.Equals(_options.Mode, "Development", StringComparison.OrdinalIgnoreCase))
        {
            return new SmsDeliveryReport(
                providerMessageId,
                SmsDeliveryStatus.Unknown,
                System.DateTime.UtcNow,
                "Simulated in Development mode.");
        }

        if (!long.TryParse(providerMessageId, out var messageId)
            || string.IsNullOrWhiteSpace(_options.BaseUrl)
            || string.IsNullOrWhiteSpace(_options.ApiToken))
        {
            return new SmsDeliveryReport(
                providerMessageId,
                SmsDeliveryStatus.Unknown,
                System.DateTime.UtcNow,
                "Invalid message ID or incomplete ApiPayamak configuration.");
        }

        try
        {
            var endpoint = BuildEndpoint(_options.StatusEndpointPath);
            using var req = CreateAuthorizedJsonPost(endpoint, new { messageid = new[] { messageId } });
            using var response = await _httpClient.SendAsync(req, ct);
            var raw = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                return new SmsDeliveryReport(providerMessageId, SmsDeliveryStatus.Unknown, System.DateTime.UtcNow, raw);
            }

            var status = ParseDeliveryStatus(raw);
            return new SmsDeliveryReport(providerMessageId, status, System.DateTime.UtcNow, raw);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ApiPayamak v3 delivery status check failed for {MessageId}", providerMessageId);
            return new SmsDeliveryReport(providerMessageId, SmsDeliveryStatus.Unknown, System.DateTime.UtcNow, ex.Message);
        }
    }

    private string BuildEndpoint(string relativePath)
    {
        var baseUrl = _options.BaseUrl.TrimEnd('/');
        var path = relativePath.TrimStart('/');
        return $"{baseUrl}/{path}";
    }

    private HttpRequestMessage CreateAuthorizedJsonPost(string endpoint, object payload)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, endpoint);
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        req.Headers.TryAddWithoutValidation("Authorization", _options.ApiToken.Trim());
        req.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        return req;
    }

    private static SmsSendResult ParseSendResponse(bool isHttpSuccess, int httpStatusCode, string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return new SmsSendResult(
                false,
                SmsDeliveryStatus.Failed,
                null,
                raw,
                $"پاسخ خالی از وب‌سرویس پیامک (HTTP {httpStatusCode}).");
        }

        try
        {
            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;

            var returnStatus = root.TryGetProperty("return", out var retEl)
                               && retEl.TryGetProperty("status", out var statusEl)
                               && statusEl.TryGetInt32(out var code)
                ? code
                : (isHttpSuccess ? 200 : httpStatusCode);

            var returnMessage = root.TryGetProperty("return", out var retMsgParent)
                                && retMsgParent.TryGetProperty("message", out var msgEl)
                ? msgEl.GetString()
                : null;

            string? messageId = null;
            if (root.TryGetProperty("data", out var dataEl) && dataEl.ValueKind == JsonValueKind.Object)
            {
                if (dataEl.TryGetProperty("messageid", out var idEl))
                {
                    messageId = idEl.ValueKind == JsonValueKind.Number
                        ? idEl.GetRawText()
                        : idEl.GetString();
                }
            }

            var accepted = isHttpSuccess && returnStatus == 200;
            return new SmsSendResult(
                accepted,
                accepted ? SmsDeliveryStatus.AcceptedByProvider : SmsDeliveryStatus.Failed,
                accepted ? messageId : null,
                raw,
                accepted ? null : returnMessage ?? $"خطا از وب‌سرویس پیامک (کد {returnStatus})");
        }
        catch (JsonException)
        {
            return new SmsSendResult(
                false,
                SmsDeliveryStatus.Failed,
                null,
                raw,
                $"پاسخ نامعتبر از وب‌سرویس پیامک (HTTP {httpStatusCode}).");
        }
    }

    private static SmsDeliveryStatus ParseDeliveryStatus(string raw)
    {
        try
        {
            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;
            if (!root.TryGetProperty("entries", out var entries)
                || entries.ValueKind != JsonValueKind.Array
                || entries.GetArrayLength() == 0)
            {
                return SmsDeliveryStatus.Unknown;
            }

            var first = entries[0];
            var statusText = first.TryGetProperty("status", out var stEl) ? stEl.GetString() : null;
            if (!string.IsNullOrWhiteSpace(statusText))
            {
                if (statusText.Contains("رسیده", StringComparison.Ordinal))
                    return SmsDeliveryStatus.Delivered;
                if (statusText.Contains("ناموفق", StringComparison.Ordinal) || statusText.Contains("لغو", StringComparison.Ordinal) || statusText.Contains("نرسیده", StringComparison.Ordinal))
                    return SmsDeliveryStatus.Failed;
                if (statusText.Contains("صف", StringComparison.Ordinal) || statusText.Contains("ارسال", StringComparison.Ordinal))
                    return SmsDeliveryStatus.Submitted;
            }

            if (first.TryGetProperty("state", out var stateEl) && stateEl.TryGetInt32(out var state))
            {
                return state switch
                {
                    10 => SmsDeliveryStatus.Delivered,
                    1 or 2 or 4 or 5 => SmsDeliveryStatus.Submitted,
                    6 or 11 or 13 or 14 => SmsDeliveryStatus.Failed,
                    _ => SmsDeliveryStatus.Unknown
                };
            }
        }
        catch (JsonException)
        {
            // Ignore malformed status response and report Unknown
        }

        return SmsDeliveryStatus.Unknown;
    }
}

/// <summary>
/// Safe placeholder renderer preventing arbitrary template injection or code execution.
/// </summary>
public static partial class SmsTemplateRenderer
{
    public static ServiceResult<string> Render(
        string templateBody,
        string allowedPlaceholdersCsv,
        IReadOnlyDictionary<string, string> values)
    {
        var allowed = allowedPlaceholdersCsv
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.Ordinal);

        var matches = PlaceholderRegex().Matches(templateBody);
        foreach (Match match in matches)
        {
            var tokenName = match.Groups[1].Value;
            if (allowed.Count > 0 && !allowed.Contains(tokenName))
            {
                return ServiceResult<string>.Fail($"متغیر '{{{tokenName}}}' در لیست متغیرهای مجاز این قالب نیست.", "INVALID_PLACEHOLDER");
            }
        }

        var rendered = PlaceholderRegex().Replace(templateBody, m =>
        {
            var key = m.Groups[1].Value;
            return values.TryGetValue(key, out var val) ? val : string.Empty;
        });

        return ServiceResult<string>.Ok(rendered);
    }

    [GeneratedRegex(@"\{([A-Za-z0-9_]+)\}")]
    private static partial Regex PlaceholderRegex();
}

public interface ISmsService
{
    Task<ServiceResult> SendOtpAsync(string mobileNumber, string plainOtpCode, Guid? userId = null, CancellationToken ct = default);
    Task<ServiceResult> SendTemplateSmsAsync(
        SmsTriggerType triggerType,
        string mobileNumber,
        IReadOnlyDictionary<string, string> variables,
        Guid? recipientUserId = null,
        string? relatedOperationType = null,
        Guid? relatedEntityId = null,
        string? idempotencyKey = null,
        Guid? initiatedByUserId = null,
        CancellationToken ct = default);
    Task<ServiceResult> SendDirectManualSmsAsync(
        string mobileNumber,
        string messageBody,
        Guid? recipientUserId,
        Guid initiatedByUserId,
        CancellationToken ct = default);
}

public sealed class SmsService : ISmsService
{
    private readonly HamnavaDbContext _db;
    private readonly ISmsProvider _provider;
    private readonly IJalaliDateService _jalali;

    public SmsService(HamnavaDbContext db, ISmsProvider provider, IJalaliDateService jalali)
    {
        _db = db;
        _provider = provider;
        _jalali = jalali;
    }

    public async Task<ServiceResult> SendOtpAsync(string mobileNumber, string plainOtpCode, Guid? userId = null, CancellationToken ct = default)
    {
        var template = await _db.SmsTemplates
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.TriggerType == SmsTriggerType.OtpVerification && t.IsActive, ct);

        var bodyToSend = template is not null
            ? template.BodyTemplate.Replace("{Code}", plainOtpCode, StringComparison.Ordinal)
            : $"کد ورود شما به هم‌نوا: {plainOtpCode}";

        var protectedLogBody = template is not null
            ? template.BodyTemplate.Replace("{Code}", "******", StringComparison.Ordinal)
            : "کد ورود شما به هم‌نوا: ******";

        var patternCode = string.IsNullOrWhiteSpace(template?.ProviderPatternCode)
            ? "2868"
            : template.ProviderPatternCode;

        var result = await _provider.SendAsync(new SmsMessageDto(
            mobileNumber,
            bodyToSend,
            IsOtp: true,
            PatternCode: patternCode,
            TemplateParameters: new Dictionary<string, string> { ["Code"] = plainOtpCode }), ct);

        _db.SmsHistories.Add(new SmsHistory
        {
            RecipientMobileNumber = mobileNumber,
            RecipientUserId = userId,
            SmsTemplateId = template?.Id,
            TriggerType = SmsTriggerType.OtpVerification,
            ProtectedMessageContent = protectedLogBody,
            ProviderName = _provider.ProviderName,
            ProviderMessageId = result.ProviderMessageId,
            ProviderRawResponse = result.RawResponse,
            DeliveryStatus = result.Status,
            RequestedAtUtc = _jalali.UtcNow,
            SubmittedAtUtc = _jalali.UtcNow,
            RelatedOperationType = "OtpAuth"
        });

        await _db.SaveChangesAsync(ct);
        return result.AcceptedByProvider
            ? ServiceResult.Ok()
            : ServiceResult.Fail(result.ErrorMessage ?? "ارسال پیامک کد تایید ناموفق بود.", "SMS_OTP_FAILED");
    }

    public async Task<ServiceResult> SendTemplateSmsAsync(
        SmsTriggerType triggerType,
        string mobileNumber,
        IReadOnlyDictionary<string, string> variables,
        Guid? recipientUserId = null,
        string? relatedOperationType = null,
        Guid? relatedEntityId = null,
        string? idempotencyKey = null,
        Guid? initiatedByUserId = null,
        CancellationToken ct = default)
    {
        if (!string.IsNullOrWhiteSpace(idempotencyKey) &&
            await _db.SmsHistories.AnyAsync(h => h.IdempotencyKey == idempotencyKey, ct))
        {
            return ServiceResult.Ok("پیامک قبلاً ارسال شده است.");
        }

        var template = await _db.SmsTemplates
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.TriggerType == triggerType && t.IsActive && t.IsAutoTriggerEnabled, ct);

        if (template is null)
        {
            return ServiceResult.Fail("قالب پیامک فعال برای این رویداد یافت نشد.", "TEMPLATE_NOT_FOUND");
        }

        var rendered = SmsTemplateRenderer.Render(template.BodyTemplate, template.AllowedPlaceholdersCsv, variables);
        if (!rendered.Succeeded || rendered.Data is null)
        {
            return ServiceResult.Fail(rendered.Message, rendered.ErrorCode);
        }

        var result = await _provider.SendAsync(new SmsMessageDto(
            mobileNumber,
            rendered.Data,
            IsOtp: false,
            PatternCode: template.ProviderPatternCode,
            TemplateParameters: variables), ct);

        _db.SmsHistories.Add(new SmsHistory
        {
            RecipientMobileNumber = mobileNumber,
            RecipientUserId = recipientUserId,
            SmsTemplateId = template.Id,
            TriggerType = triggerType,
            ProtectedMessageContent = rendered.Data,
            ProviderName = _provider.ProviderName,
            ProviderMessageId = result.ProviderMessageId,
            ProviderRawResponse = result.RawResponse,
            DeliveryStatus = result.Status,
            RequestedAtUtc = _jalali.UtcNow,
            SubmittedAtUtc = _jalali.UtcNow,
            RelatedOperationType = relatedOperationType,
            RelatedEntityId = relatedEntityId,
            IdempotencyKey = idempotencyKey,
            InitiatedByUserId = initiatedByUserId
        });

        await _db.SaveChangesAsync(ct);
        return result.AcceptedByProvider
            ? ServiceResult.Ok("پیامک با موفقیت ارسال شد.")
            : ServiceResult.Fail(result.ErrorMessage ?? "ارسال پیامک ناموفق بود.", "SMS_SEND_FAILED");
    }

    public async Task<ServiceResult> SendDirectManualSmsAsync(
        string mobileNumber,
        string messageBody,
        Guid? recipientUserId,
        Guid initiatedByUserId,
        CancellationToken ct = default)
    {
        var result = await _provider.SendAsync(new SmsMessageDto(mobileNumber, messageBody.Trim()), ct);
        _db.SmsHistories.Add(new SmsHistory
        {
            RecipientMobileNumber = mobileNumber,
            RecipientUserId = recipientUserId,
            TriggerType = SmsTriggerType.Manual,
            ProtectedMessageContent = messageBody.Trim(),
            ProviderName = _provider.ProviderName,
            ProviderMessageId = result.ProviderMessageId,
            ProviderRawResponse = result.RawResponse,
            DeliveryStatus = result.Status,
            RequestedAtUtc = _jalali.UtcNow,
            SubmittedAtUtc = _jalali.UtcNow,
            RelatedOperationType = "ManualSms",
            InitiatedByUserId = initiatedByUserId
        });

        await _db.SaveChangesAsync(ct);
        return result.AcceptedByProvider
            ? ServiceResult.Ok("پیامک با موفقیت ارسال شد.")
            : ServiceResult.Fail(result.ErrorMessage ?? "ارسال پیامک ناموفق بود.", "SMS_MANUAL_FAILED");
    }
}
