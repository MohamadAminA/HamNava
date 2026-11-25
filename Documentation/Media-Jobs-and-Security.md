# Media Processing, Background Jobs & Security Architecture

## 1. Media Upload & Asynchronous Processing (`Hamnava.Core/Media/MediaServices.cs`)
1. **Validation:** Extension allow-list (`.mp4`, `.mov`, `.jpg`, `.jpeg`, `.png`, `.webp`), strict magic-byte signature verification (`MediaSignatureValidator`), and database-configurable size caps (`MaxUploadVideoMb`, `MaxUploadImageMb`).
2. **Non-Blocking Hangfire Pipeline:** Original upload is saved with a GUID filename under `/var/lib/hamnava/media/raw` (never trusting client filenames or paths), and a Hangfire job (`media` queue) transcodes video via FFmpeg (`H.264/AAC`, `CRF 26`, `max 720p`, `-movflags +faststart`) + extracts a poster thumbnail, or resizes/compresses images to WebP via SkiaSharp.
3. **Authorized Delivery:** Served strictly via `MediaController` (`/media/{id}`, `/media/{id}/thumb`) with HTTP Range support (`enableRangeProcessing: true`) and consent/role enforcement.

## 2. Hangfire Persistent Background Jobs (`Hamnava.Core/BackgroundJobs/HamnavaBackgroundJobs.cs`)
Stored in SQL Server (`Hangfire` schema) with queues `critical`, `default`, `media`, `bulk`:
- `hamnava-lesson-reminders` (`*/15 * * * *`): Sends idempotent pre-class SMS reminders based on `SystemSettingKeys.LessonReminderHoursBefore`.
- `hamnava-birthday-discounts` (`0 6 * * *`): Evaluates Jalali birthdays (including leap-year Esfand 30) and issues one personal birthday discount per user per Jalali year.
- `hamnava-rental-expiry-reminders` (`0 8 * * *`): Dispatches idempotent rental expiration notices.
- `hamnava-cleanup-otps-payments` (`0 3 * * *`): Marks stale pending payments as expired and cleans old OTP rows.

## 3. Authentication & Security Baseline
- **Passwordless SMS OTP:** Codes hashed with HMAC-SHA256 + `Security:OtpHashPepper`, constant-time verification (`CryptographicOperations.FixedTimeEquals`), DB-configurable expiry, resend cooldown, max failed attempts, and per-mobile/IP rate limits. Never logs plaintext OTP.
- **Revocable Cookie Sessions:** `HttpOnly`, `Secure` (Production), `SameSite=Lax`, validated via `SessionValidationCookieEvents` against `UserSessions` table.
- **CSRF & Headers:** Global `AutoValidateAntiforgeryTokenAttribute`, `SecurityHeadersMiddleware`, `ForwardedHeadersMiddleware` (for real client IP behind Nginx), and persistent DataProtection key ring on Linux.
