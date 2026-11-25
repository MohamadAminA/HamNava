using System.Data;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Hamnava.Core.Common;
using Hamnava.Core.DateTime;
using Hamnava.Core.Finance;
using Hamnava.Core.Services;
using Hamnava.DataLayer.Context;
using Hamnava.Domain.Entities.Finance;
using Hamnava.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Hamnava.Core.Payments;

public sealed class PaymentGatewayOptions
{
    public const string SectionName = "PaymentGateway:Zarinpal";

    /// <summary>"Sandbox" or "Production"</summary>
    public string Environment { get; set; } = "Sandbox";
    public string MerchantId { get; set; } = string.Empty;
    public string CallbackBaseUrl { get; set; } = "https://hamnavamusic.ir";
    public int TimeoutSeconds { get; set; } = 30;

    public bool IsSandbox => !string.Equals(Environment, "Production", StringComparison.OrdinalIgnoreCase);

    public string RequestEndpoint => IsSandbox
        ? "https://sandbox.zarinpal.com/pg/v4/payment/request.json"
        : "https://payment.zarinpal.com/pg/v4/payment/request.json";

    public string VerifyEndpoint => IsSandbox
        ? "https://sandbox.zarinpal.com/pg/v4/payment/verify.json"
        : "https://payment.zarinpal.com/pg/v4/payment/verify.json";

    public string BuildStartPayUrl(string authority) => IsSandbox
        ? $"https://sandbox.zarinpal.com/pg/StartPay/{authority}"
        : $"https://payment.zarinpal.com/pg/StartPay/{authority}";
}

public sealed record ZarinpalRequestDto(
    decimal AmountIrr,
    string CallbackUrl,
    string Description,
    string? Mobile = null,
    string? OrderId = null);

public sealed record ZarinpalResponseDto(
    bool Succeeded,
    int Code,
    string? Authority,
    string? RedirectUrl,
    string? Message,
    int HttpStatusCode,
    string? RedactedRawResponse);

public sealed record ZarinpalVerificationDto(
    bool Succeeded,
    bool AlreadyVerified,
    int Code,
    string? RefId,
    string? CardPanMasked,
    string? Message,
    int HttpStatusCode);

public interface IZarinpalPaymentGateway
{
    Task<ZarinpalResponseDto> RequestPaymentAsync(ZarinpalRequestDto request, CancellationToken ct = default);
    Task<ZarinpalVerificationDto> VerifyPaymentAsync(string authority, decimal expectedAmountIrr, CancellationToken ct = default);
}

/// <summary>
/// Zarinpal REST API v4 payment gateway implementation (verified against official Zarinpal v4 docs).
/// All amounts are transmitted in IRR matching Hamnava's internal currency discipline.
/// </summary>
public sealed class ZarinpalPaymentGateway : IZarinpalPaymentGateway
{
    private readonly HttpClient _httpClient;
    private readonly PaymentGatewayOptions _options;
    private readonly ILogger<ZarinpalPaymentGateway> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public ZarinpalPaymentGateway(
        HttpClient httpClient,
        IOptions<PaymentGatewayOptions> options,
        ILogger<ZarinpalPaymentGateway> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
        _httpClient.Timeout = TimeSpan.FromSeconds(Math.Clamp(_options.TimeoutSeconds, 5, 90));
    }

    public async Task<ZarinpalResponseDto> RequestPaymentAsync(ZarinpalRequestDto request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_options.MerchantId) || _options.MerchantId.Length != 36)
        {
            return new ZarinpalResponseDto(false, -1, null, null, "شناسه پذیرنده زرین‌پال پیکربندی نشده است.", 0, null);
        }

        var payload = new
        {
            merchant_id = _options.MerchantId,
            amount = (long)decimal.Round(request.AmountIrr, 0, MidpointRounding.AwayFromZero),
            currency = "IRR",
            callback_url = request.CallbackUrl,
            description = request.Description,
            metadata = new
            {
                mobile = request.Mobile,
                order_id = request.OrderId
            }
        };

        try
        {
            using var response = await _httpClient.PostAsJsonAsync(_options.RequestEndpoint, payload, ct);
            var statusCode = (int)response.StatusCode;
            var rawBody = await response.Content.ReadAsStringAsync(ct);

            var envelope = JsonSerializer.Deserialize<ZarinpalRequestEnvelope>(rawBody, JsonOptions);
            if (response.IsSuccessStatusCode && envelope?.Data?.Code == 100 && !string.IsNullOrWhiteSpace(envelope.Data.Authority))
            {
                return new ZarinpalResponseDto(
                    true,
                    100,
                    envelope.Data.Authority,
                    _options.BuildStartPayUrl(envelope.Data.Authority),
                    envelope.Data.Message,
                    statusCode,
                    rawBody);
            }

            var errMsg = envelope?.Errors?.Message ?? envelope?.Data?.Message ?? "خطا در ایجاد تراکنش درگاه پرداخت.";
            _logger.LogWarning("Zarinpal request failed. Env={Env} HTTP={Status} Code={Code}",
                _options.Environment, statusCode, envelope?.Data?.Code ?? envelope?.Errors?.Code);

            return new ZarinpalResponseDto(false, envelope?.Errors?.Code ?? envelope?.Data?.Code ?? -1, null, null, errMsg, statusCode, rawBody);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Zarinpal payment request exception in {Environment}", _options.Environment);
            return new ZarinpalResponseDto(false, -99, null, null, "ارتباط با درگاه پرداخت برقرار نشد.", 0, null);
        }
    }

    public async Task<ZarinpalVerificationDto> VerifyPaymentAsync(string authority, decimal expectedAmountIrr, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_options.MerchantId) || _options.MerchantId.Length != 36)
        {
            return new ZarinpalVerificationDto(false, false, -1, null, null, "شناسه پذیرنده زرین‌پال پیکربندی نشده است.", 0);
        }

        var payload = new
        {
            merchant_id = _options.MerchantId,
            amount = (long)decimal.Round(expectedAmountIrr, 0, MidpointRounding.AwayFromZero),
            authority
        };

        try
        {
            using var response = await _httpClient.PostAsJsonAsync(_options.VerifyEndpoint, payload, ct);
            var statusCode = (int)response.StatusCode;
            var rawBody = await response.Content.ReadAsStringAsync(ct);
            var envelope = JsonSerializer.Deserialize<ZarinpalVerifyEnvelope>(rawBody, JsonOptions);

            var code = envelope?.Data?.Code ?? envelope?.Errors?.Code ?? -1;
            if (response.IsSuccessStatusCode && (code == 100 || code == 101))
            {
                return new ZarinpalVerificationDto(
                    true,
                    AlreadyVerified: code == 101,
                    code,
                    envelope?.Data?.RefId.ToString(),
                    envelope?.Data?.CardPan,
                    envelope?.Data?.Message,
                    statusCode);
            }

            var errMsg = envelope?.Errors?.Message ?? envelope?.Data?.Message ?? "تایید پرداخت توسط درگاه ناموفق بود.";
            _logger.LogWarning("Zarinpal verify failed. Authority={Authority} Code={Code}", authority, code);
            return new ZarinpalVerificationDto(false, false, code, null, null, errMsg, statusCode);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Zarinpal verify exception for Authority={Authority}", authority);
            return new ZarinpalVerificationDto(false, false, -99, null, null, "خطای شبکه هنگام تایید پرداخت.", 0);
        }
    }

    private sealed class ZarinpalRequestEnvelope
    {
        [JsonPropertyName("data")] public ZarinpalRequestData? Data { get; set; }
        [JsonPropertyName("errors")] public ZarinpalErrorData? Errors { get; set; }
    }

    private sealed class ZarinpalRequestData
    {
        [JsonPropertyName("code")] public int Code { get; set; }
        [JsonPropertyName("message")] public string? Message { get; set; }
        [JsonPropertyName("authority")] public string? Authority { get; set; }
    }

    private sealed class ZarinpalVerifyEnvelope
    {
        [JsonPropertyName("data")] public ZarinpalVerifyData? Data { get; set; }
        [JsonPropertyName("errors")] public ZarinpalErrorData? Errors { get; set; }
    }

    private sealed class ZarinpalVerifyData
    {
        [JsonPropertyName("code")] public int Code { get; set; }
        [JsonPropertyName("message")] public string? Message { get; set; }
        [JsonPropertyName("ref_id")] public long RefId { get; set; }
        [JsonPropertyName("card_pan")] public string? CardPan { get; set; }
    }

    private sealed class ZarinpalErrorData
    {
        [JsonPropertyName("code")] public int Code { get; set; }
        [JsonPropertyName("message")] public string? Message { get; set; }
    }
}

public sealed record InitiatePaymentInput(
    Guid UserId,
    PaymentType PaymentType,
    Guid ReferenceEntityId,
    decimal OriginalAmountIrr,
    string Description,
    string? DiscountCode = null,
    Guid? CoursePlanId = null,
    Guid? TargetTeacherUserId = null,
    string? MobileNumber = null);

public sealed record PaymentInitiationResult(
    Guid PaymentId,
    bool RequiresGatewayRedirect,
    string? RedirectUrl,
    string? Authority);

public sealed record PaymentCallbackResult(
    Guid PaymentId,
    bool Succeeded,
    bool WasDuplicateCallback,
    string? RefId,
    decimal FinalAmountIrr,
    string Message);

public interface IPaymentOrchestrationService
{
    Task<ServiceResult<PaymentInitiationResult>> InitiatePaymentAsync(InitiatePaymentInput input, CancellationToken ct = default);
    Task<ServiceResult<PaymentCallbackResult>> HandleZarinpalCallbackAsync(string authority, string status, CancellationToken ct = default);
}

/// <summary>
/// Idempotent payment workflow orchestrator. Never activates Enrollments, Rentals, or Consultations
/// on redirect alone; requires server-to-server verification and executes fulfillment atomically once.
/// </summary>
public sealed class PaymentOrchestrationService : IPaymentOrchestrationService
{
    private readonly HamnavaDbContext _db;
    private readonly IZarinpalPaymentGateway _gateway;
    private readonly IDiscountService _discountService;
    private readonly IFinancialService _financialService;
    private readonly IInstrumentRentalService _rentalService;
    private readonly IConsultationService _consultationService;
    private readonly IJalaliDateService _jalali;
    private readonly PaymentGatewayOptions _gatewayOptions;

    public PaymentOrchestrationService(
        HamnavaDbContext db,
        IZarinpalPaymentGateway gateway,
        IDiscountService discountService,
        IFinancialService financialService,
        IInstrumentRentalService rentalService,
        IConsultationService consultationService,
        IJalaliDateService jalali,
        IOptions<PaymentGatewayOptions> gatewayOptions)
    {
        _db = db;
        _gateway = gateway;
        _discountService = discountService;
        _financialService = financialService;
        _rentalService = rentalService;
        _consultationService = consultationService;
        _jalali = jalali;
        _gatewayOptions = gatewayOptions.Value;
    }

    public async Task<ServiceResult<PaymentInitiationResult>> InitiatePaymentAsync(
        InitiatePaymentInput input,
        CancellationToken ct = default)
    {
        decimal discountAmount = 0m;
        Guid? discountCodeId = null;

        if (!string.IsNullOrWhiteSpace(input.DiscountCode))
        {
            var eval = await _discountService.ValidateAndCalculateAsync(
                input.DiscountCode,
                input.UserId,
                input.OriginalAmountIrr,
                input.CoursePlanId,
                input.TargetTeacherUserId,
                ct);

            if (!eval.Succeeded || eval.Data is null)
            {
                return ServiceResult<PaymentInitiationResult>.Fail(eval.Message, eval.ErrorCode);
            }

            discountAmount = eval.Data.DiscountAmountIrr;
            discountCodeId = eval.Data.DiscountCodeId;
        }

        var finalAmount = Math.Max(0m, input.OriginalAmountIrr - discountAmount);
        var nowUtc = _jalali.UtcNow;

        var payment = new Payment
        {
            UserId = input.UserId,
            PaymentType = input.PaymentType,
            Status = PaymentStatus.Pending,
            ReferenceEntityId = input.ReferenceEntityId,
            OriginalAmountIrr = input.OriginalAmountIrr,
            DiscountAmountIrr = discountAmount,
            FinalAmountIrr = finalAmount,
            DiscountCodeId = discountCodeId,
            GatewayName = "Zarinpal",
            Description = input.Description,
            RequestedAtUtc = nowUtc,
            ExpiresAtUtc = nowUtc.AddMinutes(30),
            IdempotencyKey = $"PAY:{input.PaymentType}:{input.ReferenceEntityId}:{Guid.NewGuid():N}"
        };

        _db.Payments.Add(payment);
        await _db.SaveChangesAsync(ct);

        // 100% discounted (0 IRR) completes atomically without hitting external gateway
        if (finalAmount == 0m)
        {
            payment.Status = PaymentStatus.Verified;
            payment.VerifiedAtUtc = nowUtc;
            payment.GatewayRefId = "FREE-100PCT-DISCOUNT";
            await FulfillVerifiedPaymentInternalAsync(payment, ct);
            await _db.SaveChangesAsync(ct);

            return ServiceResult<PaymentInitiationResult>.Ok(
                new PaymentInitiationResult(payment.Id, false, null, null),
                "سفارش شما با اعمال کد تخفیف ۱۰۰ درصدی با موفقیت تایید شد.");
        }

        var callbackUrl = $"{_gatewayOptions.CallbackBaseUrl.TrimEnd('/')}/payments/zarinpal/callback";
        var gwResult = await _gateway.RequestPaymentAsync(new ZarinpalRequestDto(
            finalAmount,
            callbackUrl,
            input.Description,
            input.MobileNumber,
            payment.Id.ToString()), ct);

        _db.PaymentAttempts.Add(new PaymentAttempt
        {
            PaymentId = payment.Id,
            GatewayName = "Zarinpal",
            Authority = gwResult.Authority,
            HttpStatusCode = gwResult.HttpStatusCode,
            IsSuccessful = gwResult.Succeeded,
            ErrorMessage = gwResult.Succeeded ? null : gwResult.Message
        });

        if (!gwResult.Succeeded || string.IsNullOrWhiteSpace(gwResult.Authority))
        {
            payment.Status = PaymentStatus.Failed;
            await _db.SaveChangesAsync(ct);
            return ServiceResult<PaymentInitiationResult>.Fail(gwResult.Message ?? "خطا در اتصال به درگاه پرداخت.", "GATEWAY_REQUEST_FAILED");
        }

        payment.GatewayAuthority = gwResult.Authority;
        payment.Status = PaymentStatus.Redirected;
        await _db.SaveChangesAsync(ct);

        return ServiceResult<PaymentInitiationResult>.Ok(
            new PaymentInitiationResult(payment.Id, true, gwResult.RedirectUrl, gwResult.Authority),
            "انتقال به درگاه پرداخت زرین‌پال...");
    }

    public async Task<ServiceResult<PaymentCallbackResult>> HandleZarinpalCallbackAsync(
        string authority,
        string status,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(authority))
        {
            return ServiceResult<PaymentCallbackResult>.Fail("پارامترهای بازگشتی از درگاه نامعتبر است.", "INVALID_CALLBACK");
        }

        await using var tx = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);

        var payment = await _db.Payments.FirstOrDefaultAsync(p => p.GatewayAuthority == authority, ct);
        if (payment is null)
        {
            return ServiceResult<PaymentCallbackResult>.Fail("تراکنش موردنظر در سامانه یافت نشد.", "PAYMENT_NOT_FOUND");
        }

        // Strict idempotency on duplicate callbacks / page refreshes
        if (payment.Status == PaymentStatus.Verified && payment.IsFulfilled)
        {
            return ServiceResult<PaymentCallbackResult>.Ok(
                new PaymentCallbackResult(payment.Id, true, true, payment.GatewayRefId, payment.FinalAmountIrr, "این پرداخت قبلاً با موفقیت تایید شده است."));
        }

        if (!string.Equals(status, "OK", StringComparison.OrdinalIgnoreCase))
        {
            payment.Status = PaymentStatus.Canceled;
            payment.GatewayStatusCode = status;
            await _db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            return ServiceResult<PaymentCallbackResult>.Fail("پرداخت توسط کاربر لغو شد یا ناموفق بود.", "PAYMENT_CANCELED");
        }

        // Server-to-server verification using authoritative DB-stored FinalAmountIrr
        var verification = await _gateway.VerifyPaymentAsync(authority, payment.FinalAmountIrr, ct);

        _db.PaymentTransactions.Add(new PaymentTransaction
        {
            PaymentId = payment.Id,
            TransactionType = "Verify",
            Authority = authority,
            RefId = verification.RefId,
            GatewayCode = verification.Code,
            VerifiedAmountIrr = verification.Succeeded ? payment.FinalAmountIrr : 0m,
            RawStatus = status,
            ProcessedAtUtc = _jalali.UtcNow
        });

        if (!verification.Succeeded)
        {
            payment.Status = PaymentStatus.Failed;
            payment.GatewayStatusCode = verification.Code.ToString();
            await _db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return ServiceResult<PaymentCallbackResult>.Fail(verification.Message ?? "تایید پرداخت ناموفق بود.", "VERIFICATION_FAILED");
        }

        payment.Status = PaymentStatus.Verified;
        payment.GatewayRefId = verification.RefId;
        payment.GatewayCardPanMasked = verification.CardPanMasked;
        payment.GatewayStatusCode = verification.Code.ToString();
        payment.VerifiedAtUtc = _jalali.UtcNow;

        if (!payment.IsFulfilled)
        {
            await FulfillVerifiedPaymentInternalAsync(payment, ct);
        }

        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return ServiceResult<PaymentCallbackResult>.Ok(
            new PaymentCallbackResult(payment.Id, true, verification.AlreadyVerified, payment.GatewayRefId, payment.FinalAmountIrr, "پرداخت شما با موفقیت تایید شد."));
    }

    private async Task FulfillVerifiedPaymentInternalAsync(Payment payment, CancellationToken ct)
    {
        // Consume discount code usage atomically if one was applied
        if (payment.DiscountCodeId.HasValue)
        {
            var discount = await _db.DiscountCodes.FirstOrDefaultAsync(d => d.Id == payment.DiscountCodeId.Value, ct);
            if (discount is not null && !await _db.DiscountUsages.AnyAsync(u => u.PaymentId == payment.Id, ct))
            {
                discount.CurrentUsageCount++;
                _db.DiscountUsages.Add(new DiscountUsage
                {
                    DiscountCodeId = discount.Id,
                    UserId = payment.UserId,
                    PaymentId = payment.Id,
                    DiscountAppliedIrr = payment.DiscountAmountIrr,
                    UsedAtUtc = _jalali.UtcNow
                });
            }
        }

        switch (payment.PaymentType)
        {
            case PaymentType.Tuition:
            {
                var enrollment = await _db.Enrollments.FirstOrDefaultAsync(e => e.Id == payment.ReferenceEntityId, ct);
                if (enrollment is not null && enrollment.Status == EnrollmentStatus.PendingPayment)
                {
                    enrollment.Status = EnrollmentStatus.Active;
                    enrollment.PaymentId = payment.Id;
                    enrollment.DiscountAmountIrr = payment.DiscountAmountIrr;
                    enrollment.FinalPaidAmountIrr = payment.FinalAmountIrr;
                    enrollment.DiscountCodeId = payment.DiscountCodeId;
                }

                // Trigger first-course referral reward (exact once per invited student)
                await _financialService.GrantFirstCourseReferralRewardIfEligibleAsync(payment.Id, ct);
                break;
            }
            case PaymentType.InstrumentRental:
                await _rentalService.ActivateRentalAfterVerifiedPaymentAsync(payment.ReferenceEntityId, payment.Id, ct);
                break;
            case PaymentType.RentalRenewal:
                await _rentalService.ApplyRenewalAfterVerifiedPaymentAsync(payment.ReferenceEntityId, payment.Id, ct);
                break;
            case PaymentType.Consultation:
                await _consultationService.ConfirmBookingAfterVerifiedPaymentAsync(payment.ReferenceEntityId, payment.Id, ct);
                break;
        }

        payment.IsFulfilled = true;
        payment.FulfilledAtUtc = _jalali.UtcNow;
    }
}
