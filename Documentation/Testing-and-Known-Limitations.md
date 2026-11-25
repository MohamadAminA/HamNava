# Testing Strategy, Verification Status & Known Limitations

## 1. Automated Test Suite (`Hamnava.Tests`)
- **Unit Tests (`Hamnava.Tests/Unit/`):**
  - `FinancialAndCommissionTests.cs`: Post-discount & pre-discount session commissions, referrer share conservation, discount cap/overflow protection, instrument rental daily/monthly math, IRR->Toman display formatting.
  - `QuotaAttendanceAndJalaliTests.cs`: Exact 24-hour boundary vs 23h59m rejection, fixed 60-day quota period rollover independent of enrollment renewal, Jalali Gregorian round-trip, Persian leap-year Esfand 30 birthday matching, safe SMS placeholder validation, media magic-byte signature validation.
- **SQL Server Integration Tests (`Hamnava.Tests/Integration/`):**
  - `SqlServerBusinessRulesIntegrationTests.cs` (uses `Testcontainers.MsSql` with real `mcr.microsoft.com/mssql/server:2022-latest`):
    - Teacher horizontal isolation vs Student isolation vs Admin unrestricted access.
    - Dynamic `Classroom.MaxConcurrentSessions` (capacity = 2 allowing 2 concurrent sessions and rejecting the 3rd), classroom-instrument M:N compatibility rejection, teacher overlap rejection.
    - Duplicate Zarinpal callback idempotency, single first-course referral reward grant, Enrollment financial snapshot immutability after `CoursePlan` edits, duplicate session wallet settlement idempotency.
    - Teacher-specific discount restriction to creator teacher only.
    - Instrument rental inventory decrement, out-of-stock rejection, and rental renewal extension strictly after verified payment.

## 2. How to Build & Run Tests on a .NET 10 Host
```bash
cd Hamnava
dotnet restore Hamnava.sln
dotnet build Hamnava.sln -c Release
# Unit tests only (no Docker required):
dotnet test Hamnava.Tests/Hamnava.Tests.csproj --filter "FullyQualifiedName~Hamnava.Tests.Unit"
# Full test suite including Testcontainers SQL Server (requires Docker):
dotnet test Hamnava.Tests/Hamnava.Tests.csproj
```

## 3. Honest Verification Status & Known Limitations
- **Container Toolchain Constraint:** The build environment where this repository was authored does not have `.NET 10 SDK`, `dotnet-ef`, or `SQL Server` installed (`which dotnet` returned not found). Therefore, **all C# projects, Razor Views, and xUnit tests are `Implemented — Not yet built/executed`**. Do not assume zero compiler typos until `dotnet build` and `dotnet test` are executed on a `.NET 10 SDK` machine.
- **Initial EF Core Migration:** Because `dotnet-ef` requires the `.NET 10 SDK`, generate the first migration file (`InitialCreate`) on your `.NET 10` workstation using:
  `dotnet ef migrations add InitialCreate --project Hamnava.DataLayer --startup-project Hamnava.Web`
- **Pishgaman SMS (`pishgamrayan.com` vs `raygansms.com`):** Verified public HTTP endpoints for `raygansms.com` are implemented in `PishgamanSmsProvider` (`Mode="RayganSmsHttp"`). If your account uses `pishgamrayan.com`'s panel-gated JSON API, configure `Mode="CustomRest"` with the exact endpoint/header/key from your merchant panel (see `Documentation/External-Integrations/PishgamanSms.md`).
- **Zarinpal Sandbox Verification:** Real HTTP calls against `https://sandbox.zarinpal.com/pg/v4/payment/{request,verify}.json` require a valid Zarinpal MerchantId (36-char GUID) and outbound HTTPS testing on your `.NET 10` environment.
- **Map Embed Coordinates:** `ContactInformation.MapEmbedUrl` defaults to central Isfahan OpenStreetMap embed; update it via Admin CMS with your exact academy coordinates.
