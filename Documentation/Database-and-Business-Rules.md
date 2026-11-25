# Database Design, Business Rules & Financial Engine

## 1. Entity Groups (SQL Server EF Core 10)
All entities inherit `BaseEntity` (`Guid Id`, `DateTime CreatedAtUtc`, `DateTime? UpdatedAtUtc`, `bool IsDeleted`, `DateTime? DeletedAtUtc`, `byte[] RowVersion`) with global soft-delete filters and `DeleteBehavior.Restrict` on every foreign key to avoid SQL Server multiple cascade paths and protect financial/audit history.

- **Identity & Access:** `User`, `Role`, `UserRole`, `UserSession` (hashed token, revocable), `OtpCode` (HMAC-SHA256 hashed with server pepper), `StudentTeacherAssignment`.
- **Instruments & Rooms:** `Instrument` (rental prices & inventory counts), `Classroom` (`MaxConcurrentSessions`), `ClassroomInstrument` (M:N compatibility).
- **Education & Scheduling:** `CoursePlan`, `Enrollment` (immutable financial & commission snapshots), `ClassSession`, `TeacherAvailabilitySlot`, `RescheduleRequest`, `StudentQuotaPeriod`, `StudentQuotaUsage`, `PracticeNote`, `PracticeAssignment`, `AcademicHoliday`, `ConsultationPlan`, `ConsultationBooking`, `LearningCertificate`.
- **Finance & Loyalty:** `FinancialSettings`, `DiscountCode`, `DiscountUsage`, `Payment` (unique `GatewayAuthority`), `Wallet`, `WalletLedger` (immutable append-only with unique `IdempotencyKey`), `WalletWithdrawal`, `ReferralReward` (unique per `ReferredStudentUserId`).
- **Media, Messaging, CMS & Audit:** `PortfolioMedia`, `SmsTemplate`, `SmsLog`, `SmsCampaign`, `WebsitePage`, `WebsiteSection`, `WebsiteMenu`, `HomepageStatistic`, `HomepageFeature`, `GalleryItem`, `PublicArticle`, `PublicEvent`, `ContactInformation`, `SystemSetting`, `AuditLog`.

## 2. Key Business Rules
1. **Financial Immutability:** `Enrollment` snapshots `OriginalPriceIrr`, `DiscountAmountIrr`, `FinalPaidAmountIrr`, `TotalSessionsSnapshot`, `TeacherCommissionPercentageSnapshot`, and `CommissionBasisSnapshot` at creation so subsequent edits to `CoursePlan` or `FinancialSettings` never corrupt past enrollments.
2. **Session-Based Settlement:** Teacher earnings accrue per settled session (`Present`, or `UnauthorizedAbsence` if enabled in settings), never lump-sum upfront. Reclassifying an already settled session posts a compensating `Reversal` ledger entry rather than mutating historical ledger rows.
3. **Referrer Rewards:** Granted strictly once on a referred student's first verified course tuition payment (`ReferralReward` unique index on `ReferredStudentUserId`). Deducting referrer share from Academy share is configurable in `FinancialSettings`.
4. **Fixed 60-Day Quota:** Calculated purely from `User.QuotaStartDateGregorian` and `SystemSettingKeys.QuotaPeriodDays` (default `60`). Renewing or purchasing a new enrollment does **not** reset the student's quota period; only an Admin can perform an audited manual reset.
5. **24-Hour Authorized Absence Rule:** Student self-service authorized absence requires `session.StartAtUtc - serverNowUtc >= TimeSpan.FromHours(24)` (configurable via `SystemSettingKeys.AuthorizedAbsenceMinNoticeHours`) AND remaining quota in the active period.
6. **Scheduling & Concurrency:** Executed inside `IsolationLevel.Serializable` transactions verifying teacher overlap, student overlap, dynamic `Classroom.MaxConcurrentSessions`, classroom-instrument M:N compatibility, and academic holidays.
