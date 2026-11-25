using System.Security.Cryptography;
using System.Text;
using Hamnava.Core.Common;
using Hamnava.Core.DateTime;
using Hamnava.Core.Messaging;
using Hamnava.Core.Services;
using Hamnava.DataLayer.Context;
using Hamnava.Domain.Constants;
using Hamnava.Domain.Entities.Finance;
using Hamnava.Domain.Entities.Identity;
using Hamnava.Domain.Enums;
using Hamnava.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Hamnava.Core.Security;

public sealed class SecurityOptions
{
    public const string SectionName = "Security";
    public string OtpHashPepper { get; set; } = string.Empty;
    public bool EnableDevelopmentFixedOtpBypass { get; set; }
}

public sealed record AuthenticatedUserDto(
    Guid UserId,
    string MobileNumber,
    string FullName,
    IReadOnlyList<string> Roles,
    Guid SessionId,
    string SessionToken,
    System.DateTime SessionExpiresAtUtc);

public sealed record StudentRegistrationInput(
    string MobileNumber,
    string FirstName,
    string LastName,
    Guid InstrumentId,
    string? ReferralCode);

public interface IOtpAuthService
{
    Task<bool> IsMobileRegisteredAsync(string rawMobile, CancellationToken ct = default);
    Task<ServiceResult<int>> RequestOtpAsync(string rawMobile, string ipAddress, string purpose = "Login", CancellationToken ct = default);
    Task<ServiceResult> VerifyOtpCodeAsync(string rawMobile, string code, CancellationToken ct = default);
    Task<ServiceResult<AuthenticatedUserDto>> SignInByMobileAsync(
        string rawMobile,
        string ipAddress,
        string userAgent,
        StudentRegistrationInput? registrationIfNew = null,
        CancellationToken ct = default);
    Task<ServiceResult<AuthenticatedUserDto>> VerifyOtpAndSignInAsync(
        string rawMobile,
        string code,
        string ipAddress,
        string userAgent,
        StudentRegistrationInput? registrationIfNew = null,
        CancellationToken ct = default);
    Task<bool> IsSessionValidAsync(Guid sessionId, string sessionToken, CancellationToken ct = default);
    Task RevokeSessionAsync(Guid sessionId, CancellationToken ct = default);
}

public sealed class OtpAuthService : IOtpAuthService
{
    private readonly HamnavaDbContext _db;
    private readonly ISystemSettingsService _settings;
    private readonly IJalaliDateService _clock;
    private readonly ISmsService _smsService;
    private readonly SecurityOptions _securityOptions;
    private readonly ILogger<OtpAuthService> _logger;

    public OtpAuthService(
        HamnavaDbContext db,
        ISystemSettingsService settings,
        IJalaliDateService clock,
        ISmsService smsService,
        IOptions<SecurityOptions> securityOptions,
        ILogger<OtpAuthService> logger)
    {
        _db = db;
        _settings = settings;
        _clock = clock;
        _smsService = smsService;
        _securityOptions = securityOptions.Value;
        _logger = logger;
    }

    public async Task<bool> IsMobileRegisteredAsync(string rawMobile, CancellationToken ct = default)
    {
        if (!MobileNumber.TryParse(rawMobile, out var mobile) || mobile is null)
        {
            return false;
        }

        return await _db.Users
            .AsNoTracking()
            .AnyAsync(u => u.MobileNumber == mobile.Value, ct);
    }

    public async Task<ServiceResult<int>> RequestOtpAsync(
        string rawMobile,
        string ipAddress,
        string purpose = "Login",
        CancellationToken ct = default)
    {
        if (!MobileNumber.TryParse(rawMobile, out var mobile) || mobile is null)
        {
            return ServiceResult<int>.Fail("شماره موبایل واردشده معتبر نیست.", "INVALID_MOBILE");
        }

        var nowUtc = _clock.UtcNow;
        const int otpLength = 4;
        var expirySeconds = await _settings.GetIntAsync(SystemSettingKeys.OtpExpirationSeconds, 180, ct);
        var cooldownSeconds = await _settings.GetIntAsync(SystemSettingKeys.OtpResendCooldownSeconds, 60, ct);
        var maxAttempts = await _settings.GetIntAsync(SystemSettingKeys.OtpMaxVerificationAttempts, 5, ct);
        var maxDailyMobile = await _settings.GetIntAsync(SystemSettingKeys.OtpMaxDailyRequestsPerMobile, 10, ct);
        var maxHourlyIp = await _settings.GetIntAsync(SystemSettingKeys.OtpMaxRequestsPerIpPerHour, 20, ct);

        // Enforce IP rate limit
        var ipHourAgo = nowUtc.AddHours(-1);
        var ipCount = await _db.OtpRequests
            .AsNoTracking()
            .CountAsync(o => o.RequestIpAddress == ipAddress && o.CreatedAtUtc >= ipHourAgo, ct);
        if (ipCount >= maxHourlyIp)
        {
            return ServiceResult<int>.Fail("تعداد درخواست‌های شما بیش از حد مجاز است. لطفاً بعداً تلاش کنید.", "RATE_LIMIT_IP");
        }

        // Enforce per-mobile daily limit
        var dayAgo = nowUtc.AddHours(-24);
        var mobileDayCount = await _db.OtpRequests
            .AsNoTracking()
            .CountAsync(o => o.MobileNumber == mobile.Value && o.CreatedAtUtc >= dayAgo, ct);
        if (mobileDayCount >= maxDailyMobile)
        {
            return ServiceResult<int>.Fail("سقف درخواست روزانه کد تایید برای این شماره تکمیل شده است.", "RATE_LIMIT_MOBILE");
        }

        // Enforce resend cooldown
        var latestOtp = await _db.OtpRequests
            .OrderByDescending(o => o.CreatedAtUtc)
            .FirstOrDefaultAsync(o => o.MobileNumber == mobile.Value && !o.IsUsed, ct);
        if (latestOtp is not null)
        {
            var elapsed = (nowUtc - latestOtp.CreatedAtUtc).TotalSeconds;
            if (elapsed < cooldownSeconds)
            {
                var remaining = (int)Math.Ceiling(cooldownSeconds - elapsed);
                return ServiceResult<int>.Fail($"لطفاً {remaining} ثانیه دیگر برای ارسال مجدد کد صبر کنید.", "OTP_COOLDOWN");
            }

            // Invalidate previous active OTPs
            latestOtp.IsUsed = true;
            latestOtp.UsedAtUtc = nowUtc;
        }

        var existingUser = await _db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.MobileNumber == mobile.Value, ct);

        // Anti-enumeration: do not reveal whether mobile exists or is inactive in response
        if (existingUser is not null && existingUser.Status is UserStatus.Inactive or UserStatus.Suspended)
        {
            _logger.LogWarning("OTP request suppressed for inactive/suspended account {MaskedMobile}", mobile.ToMasked());
            return ServiceResult<int>.Ok(cooldownSeconds, "در صورت معتبر بودن شماره، کد تایید پیامک شد.");
        }

        var plainOtp = GenerateCryptoOtp(otpLength);
        var salt = Convert.ToBase64String(RandomNumberGenerator.GetBytes(16));
        var hash = ComputeHmacHash(plainOtp, salt);

        var otpRecord = new OtpRequest
        {
            MobileNumber = mobile.Value,
            UserId = existingUser?.Id,
            CodeHash = hash,
            Salt = salt,
            ExpiresAtUtc = nowUtc.AddSeconds(expirySeconds),
            MaxAttempts = maxAttempts,
            RequestIpAddress = ipAddress,
            Purpose = purpose
        };

        _db.OtpRequests.Add(otpRecord);
        await _db.SaveChangesAsync(ct);

        // Never log plainOtp in production
        await _smsService.SendOtpAsync(mobile.Value, plainOtp, existingUser?.Id, ct);

        _logger.LogInformation("OTP dispatched for {MaskedMobile}, expires in {Expiry}s", mobile.ToMasked(), expirySeconds);
        return ServiceResult<int>.Ok(cooldownSeconds, "در صورت معتبر بودن شماره، کد تایید پیامک شد.");
    }

    public async Task<ServiceResult> VerifyOtpCodeAsync(
        string rawMobile,
        string code,
        CancellationToken ct = default)
    {
        if (!MobileNumber.TryParse(rawMobile, out var mobile) || mobile is null)
        {
            return ServiceResult.Fail("شماره موبایل معتبر نیست.", "INVALID_MOBILE");
        }

        var cleanCode = _clock.ToLatinDigits((code ?? string.Empty).Trim());
        if (string.IsNullOrWhiteSpace(cleanCode) || cleanCode.Length != 4)
        {
            return ServiceResult.Fail("کد تایید ۴ رقمی را وارد کنید.", "EMPTY_OTP");
        }

        var nowUtc = _clock.UtcNow;
        var otp = await _db.OtpRequests
            .OrderByDescending(o => o.CreatedAtUtc)
            .FirstOrDefaultAsync(o => o.MobileNumber == mobile.Value && !o.IsUsed, ct);

        if (otp is null || otp.ExpiresAtUtc < nowUtc)
        {
            return ServiceResult.Fail("کد تایید منقضی شده یا نامعتبر است.", "OTP_EXPIRED");
        }

        if (otp.FailedAttempts >= otp.MaxAttempts)
        {
            otp.IsUsed = true;
            otp.UsedAtUtc = nowUtc;
            await _db.SaveChangesAsync(ct);
            return ServiceResult.Fail("تعداد تلاش‌های ناموفق بیش از حد مجاز است. لطفاً کد جدید دریافت کنید.", "OTP_LOCKED");
        }

        var computed = ComputeHmacHash(cleanCode, otp.Salt);
        var isMatch = CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(computed),
            Encoding.UTF8.GetBytes(otp.CodeHash));

        if (!isMatch)
        {
            otp.FailedAttempts++;
            if (otp.FailedAttempts >= otp.MaxAttempts)
            {
                otp.IsUsed = true;
                otp.UsedAtUtc = nowUtc;
            }

            await _db.SaveChangesAsync(ct);
            return ServiceResult.Fail("کد تایید واردشده صحیح نیست.", "OTP_INVALID");
        }

        // Mark single-use OTP consumed
        otp.IsUsed = true;
        otp.UsedAtUtc = nowUtc;
        await _db.SaveChangesAsync(ct);
        return ServiceResult.Ok();
    }

    public async Task<ServiceResult<AuthenticatedUserDto>> SignInByMobileAsync(
        string rawMobile,
        string ipAddress,
        string userAgent,
        StudentRegistrationInput? registrationIfNew = null,
        CancellationToken ct = default)
    {
        if (!MobileNumber.TryParse(rawMobile, out var mobile) || mobile is null)
        {
            return ServiceResult<AuthenticatedUserDto>.Fail("شماره موبایل معتبر نیست.", "INVALID_MOBILE");
        }

        var nowUtc = _clock.UtcNow;
        var user = await _db.Users
            .Include(u => u.UserRoles)
            .ThenInclude(ur => ur.Role)
            .FirstOrDefaultAsync(u => u.MobileNumber == mobile.Value, ct);

        if (user is null)
        {
            if (registrationIfNew is null)
            {
                return ServiceResult<AuthenticatedUserDto>.Fail("حساب کاربری با این شماره یافت نشد. لطفاً ابتدا ثبت‌نام کنید.", "USER_NOT_REGISTERED");
            }

            user = await CreateNewStudentUserAsync(mobile.Value, registrationIfNew, ct);
        }

        if (user.Status != UserStatus.Active)
        {
            return ServiceResult<AuthenticatedUserDto>.Fail("حساب کاربری شما غیرفعال است. لطفاً با آموزشگاه تماس بگیرید.", "ACCOUNT_INACTIVE");
        }

        var sessionHours = await _settings.GetIntAsync(SystemSettingKeys.AuthSessionExpirationHours, 168, ct);
        var rawSessionToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var session = new UserSession
        {
            UserId = user.Id,
            SessionTokenHash = ComputeSha256(rawSessionToken),
            IpAddress = ipAddress,
            UserAgent = userAgent.Length > 500 ? userAgent[..500] : userAgent,
            ExpiresAtUtc = nowUtc.AddHours(sessionHours),
            LastActivityAtUtc = nowUtc
        };

        _db.UserSessions.Add(session);
        await _db.SaveChangesAsync(ct);

        var roles = user.UserRoles.Select(ur => ur.Role.Name).Distinct().ToList();
        return ServiceResult<AuthenticatedUserDto>.Ok(
            new AuthenticatedUserDto(
                user.Id,
                user.MobileNumber,
                user.FullName,
                roles,
                session.Id,
                rawSessionToken,
                session.ExpiresAtUtc),
            "ورود با موفقیت انجام شد.");
    }

    public async Task<ServiceResult<AuthenticatedUserDto>> VerifyOtpAndSignInAsync(
        string rawMobile,
        string code,
        string ipAddress,
        string userAgent,
        StudentRegistrationInput? registrationIfNew = null,
        CancellationToken ct = default)
    {
        var otpCheck = await VerifyOtpCodeAsync(rawMobile, code, ct);
        if (!otpCheck.Succeeded)
        {
            return ServiceResult<AuthenticatedUserDto>.Fail(otpCheck.Message, otpCheck.ErrorCode);
        }

        return await SignInByMobileAsync(rawMobile, ipAddress, userAgent, registrationIfNew, ct);
    }

    public async Task<bool> IsSessionValidAsync(Guid sessionId, string sessionToken, CancellationToken ct = default)
    {
        var tokenHash = ComputeSha256(sessionToken);
        var nowUtc = _clock.UtcNow;

        return await _db.UserSessions
            .AsNoTracking()
            .AnyAsync(s =>
                s.Id == sessionId &&
                s.SessionTokenHash == tokenHash &&
                !s.IsRevoked &&
                s.ExpiresAtUtc > nowUtc &&
                s.User.Status == UserStatus.Active, ct);
    }

    public async Task RevokeSessionAsync(Guid sessionId, CancellationToken ct = default)
    {
        var session = await _db.UserSessions.FirstOrDefaultAsync(s => s.Id == sessionId, ct);
        if (session is not null && !session.IsRevoked)
        {
            session.IsRevoked = true;
            session.RevokedAtUtc = _clock.UtcNow;
            await _db.SaveChangesAsync(ct);
        }
    }

    private async Task<User> CreateNewStudentUserAsync(
        string normalizedMobile,
        StudentRegistrationInput input,
        CancellationToken ct)
    {
        Guid? referrerId = null;
        if (!string.IsNullOrWhiteSpace(input.ReferralCode))
        {
            var normalizedRef = input.ReferralCode.Trim().ToUpperInvariant();
            var referrer = await _db.Users
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.ReferralCode == normalizedRef && u.Status == UserStatus.Active, ct);

            // Prevent self-referral by mobile
            if (referrer is not null && referrer.MobileNumber != normalizedMobile)
            {
                referrerId = referrer.Id;
            }
        }

        var studentRole = await _db.Roles.FirstAsync(r => r.Name == RoleNames.Student, ct);

        var user = new User
        {
            MobileNumber = normalizedMobile,
            FirstName = input.FirstName.Trim(),
            LastName = input.LastName.Trim(),
            PrimaryInstrumentId = input.InstrumentId,
            ReferredByUserId = referrerId,
            Status = UserStatus.Active,
            QuotaStartDateGregorian = _clock.TehranToday,
            ReferralCode = await GenerateUniqueReferralCodeAsync(ct)
        };

        _db.Users.Add(user);
        _db.UserRoles.Add(new UserRole { User = user, Role = studentRole });
        _db.Wallets.Add(new Wallet { User = user });
        await _db.SaveChangesAsync(ct);

        // Reload navigation
        return await _db.Users
            .Include(u => u.UserRoles)
            .ThenInclude(ur => ur.Role)
            .FirstAsync(u => u.Id == user.Id, ct);
    }

    private async Task<string> GenerateUniqueReferralCodeAsync(CancellationToken ct)
    {
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        for (var attempt = 0; attempt < 10; attempt++)
        {
            var chars = new char[8];
            for (var i = 0; i < chars.Length; i++)
            {
                chars[i] = alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)];
            }

            var candidate = $"HM{new string(chars)}";
            if (!await _db.Users.AnyAsync(u => u.ReferralCode == candidate, ct))
            {
                return candidate;
            }
        }

        return $"HM{Guid.NewGuid().ToString("N")[..8].ToUpperInvariant()}";
    }

    private static string GenerateCryptoOtp(int length)
    {
        var chars = new char[length];
        for (var i = 0; i < length; i++)
        {
            chars[i] = (char)('0' + RandomNumberGenerator.GetInt32(0, 10));
        }

        return new string(chars);
    }

    private string ComputeHmacHash(string code, string salt)
    {
        var keyBytes = Encoding.UTF8.GetBytes($"{_securityOptions.OtpHashPepper}:{salt}");
        using var hmac = new HMACSHA256(keyBytes);
        var hashBytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(code));
        return Convert.ToHexString(hashBytes);
    }

    private static string ComputeSha256(string raw)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(bytes);
    }
}
