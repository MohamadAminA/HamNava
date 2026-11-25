using System.Security.Claims;
using Hamnava.Core.Messaging;
using Hamnava.Core.Security;
using Hamnava.DataLayer.Context;
using Hamnava.Domain.Constants;
using Hamnava.Domain.ValueObjects;
using Hamnava.Web.Infrastructure;
using Hamnava.Web.ViewModels;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Hamnava.Web.Controllers;

[Route("account")]
public sealed class AccountController : Controller
{
    private const string TempVerifiedMobileKey = "Hamnava.VerifiedMobile";
    private const string TempReturnUrlKey = "Hamnava.PendingReturnUrl";
    private const string TempReferralCodeKey = "Hamnava.PendingReferralCode";

    private readonly IOtpAuthService _otpAuth;
    private readonly HamnavaDbContext _db;
    private readonly SmsProviderOptions _smsOptions;

    public AccountController(
        IOtpAuthService otpAuth,
        HamnavaDbContext db,
        IOptions<SmsProviderOptions> smsOptions)
    {
        _otpAuth = otpAuth;
        _db = db;
        _smsOptions = smsOptions.Value;
    }

    private bool IsDevelopmentSmsMode =>
        string.Equals(_smsOptions.Mode, "Development", StringComparison.OrdinalIgnoreCase);

    [HttpGet("login")]
    public IActionResult Login(
        string? returnUrl = null,
        string? mobile = null,
        [FromQuery(Name = "ref")] string? referralCode = null)
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            return RedirectToDashboardForCurrentUser();
        }

        ViewData["Title"] = "ورود / ثبت‌نام در پنل هم‌نوا";
        return View(new LoginOtpViewModel
        {
            MobileNumber = mobile ?? string.Empty,
            ReturnUrl = returnUrl,
            ReferralCode = referralCode,
            IsDevelopmentSmsMode = IsDevelopmentSmsMode
        });
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login(LoginOtpViewModel model, CancellationToken ct)
    {
        model.IsDevelopmentSmsMode = IsDevelopmentSmsMode;
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        if (!MobileNumber.TryParse(model.MobileNumber, out var parsedMobile) || parsedMobile is null)
        {
            ModelState.AddModelError(nameof(model.MobileNumber), "شماره موبایل واردشده معتبر نیست.");
            return View(model);
        }

        var isRegistered = await _otpAuth.IsMobileRegisteredAsync(parsedMobile.Value, ct);
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "Unknown";

        if (IsDevelopmentSmsMode)
        {
            if (isRegistered)
            {
                var ua = Request.Headers.UserAgent.ToString();
                var signInResult = await _otpAuth.SignInByMobileAsync(parsedMobile.Value, ip, ua, null, ct);
                if (!signInResult.Succeeded || signInResult.Data is null)
                {
                    ModelState.AddModelError(string.Empty, signInResult.Message);
                    return View(model);
                }

                await SignInCookieAsync(signInResult.Data);
                return RedirectAfterSignIn(model.ReturnUrl, signInResult.Data.Roles);
            }

            TempData[TempVerifiedMobileKey] = parsedMobile.Value;
            TempData[TempReturnUrlKey] = model.ReturnUrl;
            TempData[TempReferralCodeKey] = model.ReferralCode;
            return RedirectToAction(nameof(Register));
        }

        var result = await _otpAuth.RequestOtpAsync(parsedMobile.Value, ip, isRegistered ? "Login" : "Register", ct);
        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, string.IsNullOrWhiteSpace(result.Message) ? "ارسال کد تایید ناموفق بود." : result.Message);
            return View(model);
        }

        TempData["SuccessMessage"] = result.Message;
        ViewData["Title"] = "تایید کد پیامکی | هم‌نوا";
        return View("VerifyOtp", new VerifyOtpViewModel
        {
            MobileNumber = parsedMobile.Value,
            IsRegistration = !isRegistered,
            ReturnUrl = model.ReturnUrl,
            ReferralCode = model.ReferralCode,
            CooldownSeconds = result.Data
        });
    }

    [HttpPost("resend-otp")]
    public async Task<IActionResult> ResendOtp(VerifyOtpViewModel model, CancellationToken ct)
    {
        ViewData["Title"] = "تایید کد پیامکی | هم‌نوا";

        if (!MobileNumber.TryParse(model.MobileNumber, out var parsedMobile) || parsedMobile is null)
        {
            return RedirectToAction(nameof(Login));
        }

        var isRegistered = await _otpAuth.IsMobileRegisteredAsync(parsedMobile.Value, ct);
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "Unknown";
        var result = await _otpAuth.RequestOtpAsync(parsedMobile.Value, ip, isRegistered ? "Login" : "Register", ct);

        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, string.IsNullOrWhiteSpace(result.Message) ? "ارسال مجدد کد تایید ناموفق بود." : result.Message);
            model.CooldownSeconds = 0;
        }
        else
        {
            TempData["SuccessMessage"] = "کد تایید ۴ رقمی مجدداً ارسال شد.";
            model.CooldownSeconds = result.Data;
        }

        model.MobileNumber = parsedMobile.Value;
        model.IsRegistration = !isRegistered;
        model.OtpCode = string.Empty;
        return View("VerifyOtp", model);
    }

    [HttpGet("register")]
    public async Task<IActionResult> Register([FromQuery(Name = "ref")] string? referralCode, CancellationToken ct)
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            return RedirectToDashboardForCurrentUser();
        }

        var verifiedMobile = TempData.Peek(TempVerifiedMobileKey) as string;
        if (string.IsNullOrWhiteSpace(verifiedMobile))
        {
            return RedirectToAction(nameof(Login), new { @ref = referralCode });
        }

        var instruments = await _db.Instruments
            .AsNoTracking()
            .Where(i => i.IsActive)
            .OrderBy(i => i.DisplayOrder)
            .ToListAsync(ct);

        ViewData["Title"] = "تکمیل ثبت‌نام هنرجو | آکادمی موسیقی هم‌نوا";
        return View(new RegisterStudentViewModel
        {
            MobileNumber = verifiedMobile,
            ReferralCode = referralCode ?? (TempData.Peek(TempReferralCodeKey) as string),
            ReturnUrl = TempData.Peek(TempReturnUrlKey) as string,
            Instruments = instruments
        });
    }

    [HttpPost("register")]
    public async Task<IActionResult> Register(RegisterStudentViewModel model, CancellationToken ct)
    {
        var verifiedMobile = TempData.Peek(TempVerifiedMobileKey) as string;
        if (string.IsNullOrWhiteSpace(verifiedMobile) ||
            !MobileNumber.TryParse(model.MobileNumber, out var parsedMobile) ||
            parsedMobile is null ||
            !string.Equals(verifiedMobile, parsedMobile.Value, StringComparison.Ordinal))
        {
            return RedirectToAction(nameof(Login));
        }

        model.MobileNumber = verifiedMobile;
        model.Instruments = await _db.Instruments
            .AsNoTracking()
            .Where(i => i.IsActive)
            .OrderBy(i => i.DisplayOrder)
            .ToListAsync(ct);

        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "Unknown";
        var ua = Request.Headers.UserAgent.ToString();
        var regInput = new StudentRegistrationInput(
            verifiedMobile,
            model.FirstName,
            model.LastName,
            model.InstrumentId,
            model.ReferralCode);

        var signInResult = await _otpAuth.SignInByMobileAsync(verifiedMobile, ip, ua, regInput, ct);
        if (!signInResult.Succeeded || signInResult.Data is null)
        {
            ModelState.AddModelError(string.Empty, string.IsNullOrWhiteSpace(signInResult.Message) ? "تکمیل ثبت‌نام ناموفق بود." : signInResult.Message);
            return View(model);
        }

        TempData.Remove(TempVerifiedMobileKey);
        TempData.Remove(TempReturnUrlKey);
        TempData.Remove(TempReferralCodeKey);

        await SignInCookieAsync(signInResult.Data);
        return RedirectAfterSignIn(model.ReturnUrl, signInResult.Data.Roles);
    }

    [HttpPost("verify-otp")]
    public async Task<IActionResult> VerifyOtp(VerifyOtpViewModel model, CancellationToken ct)
    {
        ViewData["Title"] = "تایید کد پیامکی | هم‌نوا";

        if (!ModelState.IsValid)
        {
            model.CooldownSeconds = 0;
            return View(model);
        }

        var otpCheck = await _otpAuth.VerifyOtpCodeAsync(model.MobileNumber, model.OtpCode, ct);
        if (!otpCheck.Succeeded)
        {
            ModelState.AddModelError(string.Empty, string.IsNullOrWhiteSpace(otpCheck.Message) ? "کد تایید نامعتبر است." : otpCheck.Message);
            model.CooldownSeconds = 0;
            return View(model);
        }

        var isRegistered = await _otpAuth.IsMobileRegisteredAsync(model.MobileNumber, ct);
        if (!isRegistered)
        {
            TempData[TempVerifiedMobileKey] = model.MobileNumber;
            TempData[TempReturnUrlKey] = model.ReturnUrl;
            TempData[TempReferralCodeKey] = model.ReferralCode;
            return RedirectToAction(nameof(Register));
        }

        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "Unknown";
        var ua = Request.Headers.UserAgent.ToString();
        var signInResult = await _otpAuth.SignInByMobileAsync(model.MobileNumber, ip, ua, null, ct);
        if (!signInResult.Succeeded || signInResult.Data is null)
        {
            ModelState.AddModelError(string.Empty, string.IsNullOrWhiteSpace(signInResult.Message) ? "ورود ناموفق بود." : signInResult.Message);
            model.CooldownSeconds = 0;
            return View(model);
        }

        await SignInCookieAsync(signInResult.Data);
        return RedirectAfterSignIn(model.ReturnUrl, signInResult.Data.Roles);
    }

    private async Task SignInCookieAsync(AuthenticatedUserDto user)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.UserId.ToString()),
            new(ClaimTypes.Name, user.FullName),
            new(ClaimTypes.MobilePhone, user.MobileNumber),
            new(HamnavaClaimTypes.SessionId, user.SessionId.ToString()),
            new(HamnavaClaimTypes.SessionToken, user.SessionToken)
        };

        foreach (var role in user.Roles)
        {
            claims.Add(new Claim(ClaimTypes.Role, role));
        }

        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        var principal = new ClaimsPrincipal(identity);

        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            principal,
            new AuthenticationProperties
            {
                IsPersistent = true,
                ExpiresUtc = new DateTimeOffset(user.SessionExpiresAtUtc, TimeSpan.Zero)
            });
    }

    private IActionResult RedirectAfterSignIn(string? returnUrl, IReadOnlyList<string> roles)
    {
        if (!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl))
        {
            return Redirect(returnUrl);
        }

        return RedirectToRoleDashboard(roles);
    }

    [Authorize]
    [HttpPost("logout")]
    public async Task<IActionResult> Logout(CancellationToken ct)
    {
        var sessionIdStr = User.FindFirstValue(HamnavaClaimTypes.SessionId);
        if (Guid.TryParse(sessionIdStr, out var sessionId))
        {
            await _otpAuth.RevokeSessionAsync(sessionId, ct);
        }

        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToAction("Index", "Home");
    }

    [HttpGet("access-denied")]
    public IActionResult AccessDenied()
    {
        ViewData["Title"] = "عدم دسترسی مجاز | هم‌نوا";
        Response.StatusCode = StatusCodes.Status403Forbidden;
        return View();
    }

    [Authorize]
    [HttpGet("/dashboard")]
    public IActionResult DashboardRedirect() => RedirectToDashboardForCurrentUser();

    private IActionResult RedirectToDashboardForCurrentUser()
    {
        var roles = User.FindAll(ClaimTypes.Role).Select(r => r.Value).ToList();
        return RedirectToRoleDashboard(roles);
    }

    private IActionResult RedirectToRoleDashboard(IReadOnlyList<string> roles)
    {
        if (roles.Contains(RoleNames.Admin))
        {
            return RedirectToAction("Index", "Dashboard", new { area = "Admin" });
        }
        if (roles.Contains(RoleNames.Teacher))
        {
            return RedirectToAction("Index", "Dashboard", new { area = "Teacher" });
        }
        if (roles.Contains(RoleNames.Student))
        {
            return RedirectToAction("Index", "Dashboard", new { area = "Student" });
        }
        if (roles.Contains(RoleNames.Referrer))
        {
            return RedirectToAction("Index", "Dashboard", new { area = "Referrer" });
        }

        return RedirectToAction("Index", "Home");
    }
}
