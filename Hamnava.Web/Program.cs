using Hamnava.Core;
using Hamnava.Core.BackgroundJobs;
using Hamnava.DataLayer;
using Hamnava.DataLayer.Context;
using Hamnava.DataLayer.Seed;
using Hamnava.Domain.Constants;
using Hamnava.Web.Infrastructure;
using Hangfire;
using Hangfire.SqlServer;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// Structured Serilog logging (sensitive data is never logged)
builder.Host.UseSerilog((ctx, services, lc) => lc
    .ReadFrom.Configuration(ctx.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .WriteTo.File(
        path: "logs/hamnava-.log",
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 30,
        shared: true));

// Forwarded headers for Nginx reverse proxy (prevents RemoteIpAddress from always reading 127.0.0.1)
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

// Persist DataProtection keys across restarts on Linux
var keyRingPath = builder.Configuration["Security:DataProtectionKeyRingPath"];
if (!string.IsNullOrWhiteSpace(keyRingPath))
{
    Directory.CreateDirectory(keyRingPath);
    builder.Services.AddDataProtection()
        .PersistKeysToFileSystem(new DirectoryInfo(keyRingPath))
        .SetApplicationName("HamnavaMusicAcademy");
}

// Layered N-Tier DI registration
builder.Services.AddHamnavaDataLayer(builder.Configuration);
builder.Services.AddHamnavaCore(builder.Configuration);

// ASP.NET Core MVC with global CSRF protection on unsafe HTTP methods
builder.Services.AddControllersWithViews(options =>
{
    options.Filters.Add(new AutoValidateAntiforgeryTokenAttribute());
});

// Passwordless Cookie Authentication backed by revocable server-side UserSessions
builder.Services.AddScoped<SessionValidationCookieEvents>();
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "Hamnava.Auth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
            ? CookieSecurePolicy.SameAsRequest
            : CookieSecurePolicy.Always;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.LoginPath = "/account/login";
        options.LogoutPath = "/account/logout";
        options.AccessDeniedPath = "/account/access-denied";
        options.SlidingExpiration = true;
        options.ExpireTimeSpan = TimeSpan.FromDays(7);
        options.EventsType = typeof(SessionValidationCookieEvents);
    });

builder.Services.AddAuthorizationBuilder()
    .AddPolicy("AdminOnly", p => p.RequireRole(RoleNames.Admin))
    .AddPolicy("TeacherOrAdmin", p => p.RequireRole(RoleNames.Teacher, RoleNames.Admin))
    .AddPolicy("StudentOrAdmin", p => p.RequireRole(RoleNames.Student, RoleNames.Admin))
    .AddPolicy("ReferrerOrAdmin", p => p.RequireRole(RoleNames.Referrer, RoleNames.Admin));

// Persistent Hangfire with SQL Server storage
var hangfireConn = builder.Configuration.GetConnectionString("HangfireConnection");
if (string.IsNullOrWhiteSpace(hangfireConn))
{
    hangfireConn = builder.Configuration.GetConnectionString("DefaultConnection");
}
if (string.IsNullOrWhiteSpace(hangfireConn))
{
    throw new InvalidOperationException("Hangfire SQL Server connection string is missing.");
}

builder.Services.AddHangfire(config => config
    .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
    .UseSimpleAssemblyNameTypeSerializer()
    .UseRecommendedSerializerSettings()
    .UseSqlServerStorage(hangfireConn, new SqlServerStorageOptions
    {
        CommandBatchMaxTimeout = TimeSpan.FromMinutes(5),
        SlidingInvisibilityTimeout = TimeSpan.FromMinutes(5),
        QueuePollInterval = TimeSpan.FromSeconds(15),
        UseRecommendedIsolationLevel = true,
        DisableGlobalLocks = true,
        SchemaName = "Hangfire"
    }));

builder.Services.AddHangfireServer(options =>
{
    options.WorkerCount = Math.Max(2, Environment.ProcessorCount);
    options.Queues = ["critical", "default", "media", "bulk"];
});

// Health Checks (liveness vs readiness)
builder.Services.AddHealthChecks()
    .AddDbContextCheck<HamnavaDbContext>("sqlserver", tags: ["ready"])
    .AddCheck<MediaStorageHealthCheck>("media_storage", tags: ["ready"]);

var app = builder.Build();

// Controlled Development-only auto-migration & idempotent seeding (NEVER auto-migrate Production!)
if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<HamnavaDbContext>();
    await db.Database.MigrateAsync();
    var seeder = scope.ServiceProvider.GetRequiredService<HamnavaDbSeeder>();
    await seeder.SeedBaselineAsync(
        builder.Configuration["Hamnava:InitialAdminMobile"],
        builder.Configuration["Hamnava:InitialAdminFirstName"] ?? "مدیر",
        builder.Configuration["Hamnava:InitialAdminLastName"] ?? "سیستم");
}

app.UseForwardedHeaders();
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseMiddleware<SecurityHeadersMiddleware>();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = ctx =>
    {
        var headers = ctx.Context.Response.Headers;
        var ct = headers.ContentType.ToString();
        if (ct.StartsWith("video/", StringComparison.OrdinalIgnoreCase) ||
            ct.StartsWith("font/", StringComparison.OrdinalIgnoreCase) ||
            ct.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
        {
            headers.CacheControl = "public, max-age=604800";
        }
    }
});

app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

// Admin-protected Hangfire Dashboard
app.UseHangfireDashboard("/dashboard/admin/jobs", new DashboardOptions
{
    Authorization = [new HangfireAdminAuthorizationFilter()],
    DashboardTitle = "هم‌نوا — صف پردازش‌های پس‌زمینه"
});

// Register idempotent Hangfire recurring jobs
RecurringJob.AddOrUpdate<IHamnavaRecurringJobs>(
    "hamnava-lesson-reminders",
    j => j.DispatchUpcomingLessonRemindersAsync(CancellationToken.None),
    "*/15 * * * *");

RecurringJob.AddOrUpdate<IHamnavaRecurringJobs>(
    "hamnava-birthday-discounts",
    j => j.ProcessDailyBirthdayDiscountsAsync(CancellationToken.None),
    "0 6 * * *");

RecurringJob.AddOrUpdate<IHamnavaRecurringJobs>(
    "hamnava-rental-expiry-reminders",
    j => j.DispatchRentalExpirationRemindersAsync(CancellationToken.None),
    "0 8 * * *");

RecurringJob.AddOrUpdate<IHamnavaRecurringJobs>(
    "hamnava-cleanup-otps-payments",
    j => j.CleanupExpiredOtpsAndPendingPaymentsAsync(CancellationToken.None),
    "0 3 * * *");

// Public Health Endpoints (sanitized, no internal details exposed publicly)
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = c => c.Tags.Contains("ready") });
app.MapHealthChecks("/health", new HealthCheckOptions { Predicate = c => c.Tags.Contains("ready") });

// Centralized Role-Based Area Routing (/dashboard/{area}/{controller}/{action}/{id?})
app.MapAreaControllerRoute(
    name: "admin_area",
    areaName: "Admin",
    pattern: "dashboard/admin/{controller=Dashboard}/{action=Index}/{id?}");

app.MapAreaControllerRoute(
    name: "teacher_area",
    areaName: "Teacher",
    pattern: "dashboard/teacher/{controller=Dashboard}/{action=Index}/{id?}");

app.MapAreaControllerRoute(
    name: "student_area",
    areaName: "Student",
    pattern: "dashboard/student/{controller=Dashboard}/{action=Index}/{id?}");

app.MapAreaControllerRoute(
    name: "referrer_area",
    areaName: "Referrer",
    pattern: "dashboard/referrer/{controller=Dashboard}/{action=Index}/{id?}");

// SEO & Default Public Website Routes
app.MapControllerRoute(name: "robots", pattern: "robots.txt", defaults: new { controller = "Home", action = "RobotsTxt" });
app.MapControllerRoute(name: "sitemap", pattern: "sitemap.xml", defaults: new { controller = "Home", action = "SitemapXml" });
app.MapControllerRoute(name: "default", pattern: "{controller=Home}/{action=Index}/{id?}");

await app.RunAsync();

public partial class Program;
