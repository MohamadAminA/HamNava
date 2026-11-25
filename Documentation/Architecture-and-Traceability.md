# Hamnava Architecture & Requirements Traceability

## 1. Layered N-Tier Solution Overview
Hamnava (`https://hamnavamusic.ir`) is architected as a clean, modular, database-driven **ASP.NET Core MVC (.NET 10)** solution with strict separation of concerns:

| Project | Responsibilities | Dependencies |
|---|---|---|
| `Hamnava.Domain` | Pure Entities, Value Objects (`MobileNumber`, `MoneyIrr`), Enums, Constants (`RoleNames`, `SystemSettingKeys`), `ISoftDeletable` | None (zero EF Core, zero ASP.NET Core) |
| `Hamnava.DataLayer` | `HamnavaDbContext`, Fluent API Configurations, Global Soft-Delete Filters, RowVersion concurrency tokens, Idempotent Baseline Seeder (`HamnavaDbSeeder`) | `Hamnava.Domain`, EF Core 10, SQL Server |
| `Hamnava.Core` | Business Engines: `SchedulingService`, `QuotaService`, `AttendanceService`, `FinancialService`, `DiscountService`, `InstrumentRentalService`, `ConsultationService`, `OtpAuthService`, `AuthorizationScopeService`, `ZarinpalPaymentGateway`, `PishgamanSmsProvider`, `MediaProcessingService` (FFmpeg/SkiaSharp), `QuestPdfCertificateService`, `SkiaStoryImageService`, `HamnavaBackgroundJobs` | `Hamnava.Domain`, `Hamnava.DataLayer`, Hangfire, QuestPDF, QRCoder, SkiaSharp |
| `Hamnava.Web` | Public SEO-optimized Razor Website (exact 10-section Figma Homepage) + 4 Role-Based Areas (`Admin`, `Teacher`, `Student`, `Referrer`), custom CSS3 design tokens (`tokens.css`, `public-site.css`, `dashboard.css`), Vanilla JS (`app.js`, `jalali-calendar.js`) | `Hamnava.Core`, `Hamnava.DataLayer`, Hangfire.AspNetCore, Serilog |
| `Hamnava.Tests` | xUnit deterministic Unit Tests + Real SQL Server `Testcontainers.MsSql` Integration Tests | `Hamnava.*`, xUnit, FluentAssertions, Moq, Testcontainers.MsSql |

## 2. Hard Architecture Invariants Verified
- **100% Database-Driven:** Course plans, prices, consultation plans, instruments, rental inventory, classrooms, dynamic `MaxConcurrentSessions` (never hardcoded to `2`), classroom-instrument M:N compatibility, commission percentages, quota periods/limits, SMS templates, CMS sections, homepage statistics, menus, and contact info are persisted in SQL Server.
- **Single Internal Currency:** Every monetary column is stored in **IRR (`decimal(18,0)`)**. Display conversion to Persian Toman happens exclusively through `CurrencyFormatter.FormatToman(...)`.
- **Jalali Calendar Consistency:** Every timestamp is stored in UTC (`DateTime`) or Gregorian `DateOnly` and converted strictly through `IJalaliDateService` (`Asia/Tehran`, `PersianCalendar`, including leap-year Esfand 30 birthday matching).
- **Zero Prohibited Technologies:** No React, Angular, Vue, Next.js, Blazor, Node.js backend, Bootstrap, or Tailwind CSS inside `/Hamnava/`.
- **Strict Role & Horizontal Isolation:** Enforced server-side in `AuthorizationScopeService` across Controllers, Calendar, Media Streaming, Certificates, Practice Notes, and Attendance.

## 3. Requirements Traceability Matrix (Core Capabilities)
| Master Prompt Area | Primary Implementation |
|---|---|
| Passwordless SMS OTP & Revocable DB Sessions | `Hamnava.Core/Security/OtpAuthService.cs`, `AccountController.cs`, `SessionValidationCookieEvents` |
| Dynamic Room Capacity & Conflict-Free Drag & Drop | `Hamnava.Core/Scheduling/SchedulingService.cs`, `CalendarController.cs`, `wwwroot/js/jalali-calendar.js` |
| Fixed 60-Day Quota Independent of Renewal | `Hamnava.Core/Services/QuotaService.cs` |
| 24h Authorized Absence Rule & Settlement Reversals | `Hamnava.Core/Services/AttendanceService.cs`, `FinancialService.cs` |
| Session-Based Commission & Immutable Ledger | `Hamnava.Core/Finance/FinancialService.cs`, `WalletLedger` unique `IdempotencyKey` |
| Zarinpal API v4 (Sandbox/Production) & Idempotent Callback | `Hamnava.Core/Payments/ZarinpalPaymentGateway.cs`, `PaymentsController.cs` |
| Pishgaman SMS (`RayganSmsHttp`, `CustomRest`, `Development`) | `Hamnava.Core/Messaging/SmsServices.cs` |
| Async FFmpeg Video Compression & Authorized Range Streaming | `Hamnava.Core/Media/MediaServices.cs`, `MediaController.cs` |
| QuestPDF Verifiable Certificate + 9:16 Instagram Story | `Hamnava.Core/Reports/CertificateAndStoryServices.cs`, `CertificatesController.cs`, `HomeController.cs` |
| 10-Section Figma Public Homepage (DB-Driven) | `HomeController.cs`, `Views/Home/Index.cshtml`, `wwwroot/css/public-site.css` |
| Hangfire Persistent Recurring Jobs (SQL Server) | `Hamnava.Core/BackgroundJobs/HamnavaBackgroundJobs.cs`, `Program.cs` |
