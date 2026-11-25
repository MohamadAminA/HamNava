using System.Security.Claims;
using Hamnava.Core.Media;
using Hamnava.Core.Security;
using Hamnava.Domain.Constants;
using Hangfire.Dashboard;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace Hamnava.Web.Infrastructure;

public static class HamnavaClaimTypes
{
    public const string SessionId = "hamnava:session_id";
    public const string SessionToken = "hamnava:session_token";
}

public sealed class CorrelationIdMiddleware
{
    private const string HeaderName = "X-Correlation-Id";
    private readonly RequestDelegate _next;

    public CorrelationIdMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = context.Request.Headers[HeaderName].FirstOrDefault() ?? Guid.NewGuid().ToString("N");
        context.Items[HeaderName] = correlationId;
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[HeaderName] = correlationId;
            return Task.CompletedTask;
        });

        await _next(context);
    }
}

public sealed class SecurityHeadersMiddleware
{
    private readonly RequestDelegate _next;

    public SecurityHeadersMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context)
    {
        var headers = context.Response.Headers;
        headers["X-Content-Type-Options"] = "nosniff";
        headers["X-Frame-Options"] = "SAMEORIGIN";
        headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
        headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
        await _next(context);
    }
}

public sealed class HangfireAdminAuthorizationFilter : IDashboardAuthorizationFilter
{
    public bool Authorize(DashboardContext context)
    {
        var httpContext = context.GetHttpContext();
        return httpContext.User.Identity?.IsAuthenticated == true &&
               httpContext.User.IsInRole(RoleNames.Admin);
    }
}

public sealed class SessionValidationCookieEvents : CookieAuthenticationEvents
{
    private readonly IOtpAuthService _authService;

    public SessionValidationCookieEvents(IOtpAuthService authService) => _authService = authService;

    public override async Task ValidatePrincipal(CookieValidatePrincipalContext context)
    {
        var sessionIdStr = context.Principal?.FindFirstValue(HamnavaClaimTypes.SessionId);
        var sessionToken = context.Principal?.FindFirstValue(HamnavaClaimTypes.SessionToken);

        if (!Guid.TryParse(sessionIdStr, out var sessionId) || string.IsNullOrWhiteSpace(sessionToken))
        {
            context.RejectPrincipal();
            await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return;
        }

        var isValid = await _authService.IsSessionValidAsync(sessionId, sessionToken, context.HttpContext.RequestAborted);
        if (!isValid)
        {
            context.RejectPrincipal();
            await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        }
    }
}

public sealed class MediaStorageHealthCheck : IHealthCheck
{
    private readonly MediaStorageOptions _options;

    public MediaStorageHealthCheck(IOptions<MediaStorageOptions> options) => _options = options.Value;

    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var root = string.IsNullOrWhiteSpace(_options.RootPath)
                ? Path.Combine(AppContext.BaseDirectory, "storage", "media")
                : _options.RootPath;
            Directory.CreateDirectory(root);
            return Task.FromResult(HealthCheckResult.Healthy("Media storage directory is accessible."));
        }
        catch (Exception ex)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy("Media storage unavailable.", ex));
        }
    }
}

public static class HttpContextActorExtensions
{
    public static ActorContext ToActorContext(this HttpContext context)
    {
        var userIdStr = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userIdStr, out var userId))
        {
            throw new UnauthorizedAccessException("کاربر احراز هویت نشده است.");
        }

        var roles = context.User.FindAll(ClaimTypes.Role)
            .Select(c => c.Value)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var ip = context.Connection.RemoteIpAddress?.ToString() ?? "Unknown";
        var ua = context.Request.Headers.UserAgent.ToString();
        var corr = context.Items["X-Correlation-Id"]?.ToString();

        return new ActorContext(userId, roles, ip, ua, corr);
    }
}
