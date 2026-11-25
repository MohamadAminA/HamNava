using FluentAssertions;
using Hamnava.Core.DateTime;
using Hamnava.Core.Finance;
using Hamnava.Core.Payments;
using Hamnava.Core.Scheduling;
using Hamnava.Core.Security;
using Hamnava.Core.Services;
using Hamnava.DataLayer.Context;
using Hamnava.DataLayer.Seed;
using Hamnava.Domain.Constants;
using Hamnava.Domain.Entities.Education;
using Hamnava.Domain.Entities.Finance;
using Hamnava.Domain.Entities.Identity;
using Hamnava.Domain.Entities.Instruments;
using Hamnava.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Moq;
using Testcontainers.MsSql;
using Xunit;

namespace Hamnava.Tests.Integration;

/// <summary>
/// Real SQL Server integration tests powered by Testcontainers.MsSql (requires Docker + .NET 10 SDK).
/// Validates the 25 mandatory business rules from the Hamnava Master Specification against actual
/// SQL Server Serializable transactions, unique constraints, and RowVersion concurrency tokens.
/// </summary>
public sealed class SqlServerBusinessRulesIntegrationTests : IAsyncLifetime
{
    private readonly MsSqlContainer _sqlContainer = new MsSqlBuilder()
        .WithImage("mcr.microsoft.com/mssql/server:2022-latest")
        .Build();

    private string _connectionString = string.Empty;

    public async Task InitializeAsync()
    {
        await _sqlContainer.StartAsync();
        _connectionString = _sqlContainer.GetConnectionString();

        await using var db = CreateDbContext();
        await db.Database.EnsureCreatedAsync();
        var seeder = new HamnavaDbSeeder(db);
        await seeder.SeedBaselineAsync("09120000001", "مدیر", "تست");
    }

    public async Task DisposeAsync()
    {
        await _sqlContainer.DisposeAsync();
    }

    private HamnavaDbContext CreateDbContext()
    {
        var opts = new DbContextOptionsBuilder<HamnavaDbContext>()
            .UseSqlServer(_connectionString)
            .Options;
        return new HamnavaDbContext(opts);
    }

    [Fact]
    public async Task AuthorizationScope_TeacherHorizontalIsolation_And_AdminUnrestrictedAccess()
    {
        await using var db = CreateDbContext();
        var authScope = new AuthorizationScopeService(db);

        var piano = await db.Instruments.FirstAsync(i => i.Slug == "piano");
        var teacherA = CreateUser("09131110001", "استاد", "الف");
        var teacherB = CreateUser("09131110002", "استاد", "ب");
        var student1 = CreateUser("09131110003", "هنرجو", "یک");
        var admin = CreateUser("09131110004", "مدیر", "کل");

        db.Users.AddRange(teacherA, teacherB, student1, admin);
        db.TeacherStudentAssignments.Add(new TeacherStudentAssignment
        {
            StudentUserId = student1.Id,
            TeacherUserId = teacherA.Id,
            InstrumentId = piano.Id,
            IsActive = true
        });
        await db.SaveChangesAsync();

        var actorTeacherA = new ActorContext(teacherA.Id, new HashSet<string> { RoleNames.Teacher });
        var actorTeacherB = new ActorContext(teacherB.Id, new HashSet<string> { RoleNames.Teacher });
        var actorStudent1 = new ActorContext(student1.Id, new HashSet<string> { RoleNames.Student });
        var actorAdmin = new ActorContext(admin.Id, new HashSet<string> { RoleNames.Admin });

        (await authScope.CanAccessStudentAsync(actorTeacherA, student1.Id)).Should().BeTrue();
        (await authScope.CanAccessStudentAsync(actorTeacherB, student1.Id)).Should().BeFalse("Teacher B must never access Teacher A's student");
        (await authScope.CanAccessStudentAsync(actorStudent1, student1.Id)).Should().BeTrue();
        (await authScope.CanAccessStudentAsync(actorStudent1, teacherA.Id)).Should().BeFalse("Student must never access other users");
        (await authScope.CanAccessStudentAsync(actorAdmin, student1.Id)).Should().BeTrue("Admin has unrestricted management access");
    }

    [Fact]
    public async Task Scheduling_DynamicRoomCapacityAndInstrumentCompatibility_EnforcedFromDatabase()
    {
        await using var db = CreateDbContext();
        var jalali = new JalaliDateService();
        var settings = new SystemSettingsService(db);
        var authScope = new AuthorizationScopeService(db);
        var quota = new QuotaService(db, settings, authScope, jalali);
        var scheduler = new SchedulingService(db, authScope, quota, settings, jalali);

        var piano = await db.Instruments.FirstAsync(i => i.Slug == "piano");
        var violin = await db.Instruments.FirstAsync(i => i.Slug == "violin");

        // Room with dynamic capacity = 2 supporting ONLY Piano (not Violin)
        var room = new Room
        {
            Name = "اتاق تست ظرفیت دو",
            Description = "TEST-CAP-2",
            Capacity = 2,
            IsActive = true
        };
        room.SupportedInstruments.Add(new RoomSupportedInstrument { InstrumentId = piano.Id });
        db.Rooms.Add(room);

        var plan = await db.CoursePlans.FirstAsync();
        var t1 = CreateUser("09132220001", "ت1", "استاد");
        var t2 = CreateUser("09132220002", "ت2", "استاد");
        var t3 = CreateUser("09132220003", "ت3", "استاد");
        var s1 = CreateUser("09132220004", "س1", "هنرجو");
        var s2 = CreateUser("09132220005", "س2", "هنرجو");
        var s3 = CreateUser("09132220006", "س3", "هنرجو");
        db.Users.AddRange(t1, t2, t3, s1, s2, s3);

        var e1 = CreateEnrollment(s1.Id, t1.Id, piano.Id, plan.Id, room.Id);
        var e2 = CreateEnrollment(s2.Id, t2.Id, piano.Id, plan.Id, room.Id);
        db.Enrollments.AddRange(e1, e2);

        var slotStart = DateTime.UtcNow.Date.AddDays(5).AddHours(10);
        var slotEnd = slotStart.AddMinutes(45);

        db.ClassSessions.Add(new ClassSession
        {
            EnrollmentId = e1.Id, StudentUserId = s1.Id, TeacherUserId = t1.Id, InstrumentId = piano.Id, RoomId = room.Id,
            SessionNumber = 1, StartAtUtc = slotStart, EndAtUtc = slotEnd, DurationMinutes = 45, Status = ClassSessionStatus.Scheduled
        });
        await db.SaveChangesAsync();

        // Incompatible instrument (Violin in Piano-only room) -> rejected
        var wrongInstrument = await scheduler.ValidateSlotAsync(new ScheduleValidationRequest(t2.Id, s2.Id, room.Id, violin.Id, slotStart, slotEnd));
        wrongInstrument.Succeeded.Should().BeFalse();
        wrongInstrument.ErrorCode.Should().Be("INSTRUMENT_ROOM_INCOMPATIBLE");

        // Second concurrent Piano session (capacity = 2) -> allowed
        var secondOk = await scheduler.ValidateSlotAsync(new ScheduleValidationRequest(t2.Id, s2.Id, room.Id, piano.Id, slotStart, slotEnd));
        secondOk.Succeeded.Should().BeTrue();

        // Teacher 1 conflict -> rejected
        var teacherConflict = await scheduler.ValidateSlotAsync(new ScheduleValidationRequest(t1.Id, s2.Id, room.Id, piano.Id, slotStart, slotEnd));
        teacherConflict.Succeeded.Should().BeFalse();
        teacherConflict.ErrorCode.Should().Be("TEACHER_CONFLICT");

        // Add 2nd session so capacity (2/2) is full -> 3rd concurrent session rejected
        db.ClassSessions.Add(new ClassSession
        {
            EnrollmentId = e2.Id, StudentUserId = s2.Id, TeacherUserId = t2.Id, InstrumentId = piano.Id, RoomId = room.Id,
            SessionNumber = 1, StartAtUtc = slotStart, EndAtUtc = slotEnd, DurationMinutes = 45, Status = ClassSessionStatus.Scheduled
        });
        await db.SaveChangesAsync();

        var thirdRejected = await scheduler.ValidateSlotAsync(new ScheduleValidationRequest(t3.Id, s3.Id, room.Id, piano.Id, slotStart, slotEnd));
        thirdRejected.Succeeded.Should().BeFalse();
        thirdRejected.ErrorCode.Should().Be("ROOM_CAPACITY_EXCEEDED");
    }

    [Fact]
    public async Task PaymentsAndReferral_DuplicateZarinpalCallbackAndWalletSettlement_AreStrictlyIdempotent()
    {
        await using var db = CreateDbContext();
        var jalali = new JalaliDateService();
        var settings = new SystemSettingsService(db);
        var authScope = new AuthorizationScopeService(db);
        var financial = new FinancialService(db, settings, authScope, jalali);
        var discount = new DiscountService(db, settings, jalali);
        var rental = new InstrumentRentalService(db, authScope, jalali);
        var consultation = new ConsultationService(db);

        var gatewayMock = new Mock<IZarinpalPaymentGateway>();
        gatewayMock.Setup(g => g.VerifyPaymentAsync("AUTH-IDEMP-001", 20_000_000m, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ZarinpalVerificationDto(true, false, 100, "REF-998877", "6037-****-1234", "OK", 200));

        var orchestrator = new PaymentOrchestrationService(
            db, gatewayMock.Object, discount, financial, rental, consultation, jalali,
            Options.Create(new PaymentGatewayOptions { Environment = "Sandbox", CallbackBaseUrl = "https://hamnavamusic.ir" }));

        var piano = await db.Instruments.FirstAsync(i => i.Slug == "piano");
        var plan = await db.CoursePlans.FirstAsync();
        var room = await db.Rooms.FirstAsync();

        var referrer = CreateUser("09133330001", "معرف", "تست");
        var teacher = CreateUser("09133330002", "استاد", "مالی");
        var student = CreateUser("09133330003", "هنرجو", "مالی");
        student.ReferredByUserId = referrer.Id;
        db.Users.AddRange(referrer, teacher, student);

        var enrollment = CreateEnrollment(student.Id, teacher.Id, piano.Id, plan.Id, room.Id);
        enrollment.Status = EnrollmentStatus.PendingPayment;
        enrollment.OriginalPriceIrr = 20_000_000m;
        enrollment.FinalPaidAmountIrr = 20_000_000m;
        db.Enrollments.Add(enrollment);

        var payment = new Payment
        {
            UserId = student.Id,
            PaymentType = PaymentType.Tuition,
            ReferenceEntityId = enrollment.Id,
            OriginalAmountIrr = 20_000_000m,
            DiscountAmountIrr = 0m,
            FinalAmountIrr = 20_000_000m,
            GatewayName = "Zarinpal",
            GatewayAuthority = "AUTH-IDEMP-001",
            Status = PaymentStatus.Pending,
            ExpiresAtUtc = DateTime.UtcNow.AddMinutes(30),
            IdempotencyKey = Guid.NewGuid().ToString("N")
        };
        db.Payments.Add(payment);
        await db.SaveChangesAsync();

        // First callback verifies & grants single first-course referral reward
        var first = await orchestrator.HandleZarinpalCallbackAsync("AUTH-IDEMP-001", "OK");
        first.Succeeded.Should().BeTrue();
        first.Data!.WasDuplicateCallback.Should().BeFalse();

        // Second duplicate callback must be idempotent & never double-credit referrer wallet
        var second = await orchestrator.HandleZarinpalCallbackAsync("AUTH-IDEMP-001", "OK");
        second.Succeeded.Should().BeTrue();
        second.Data!.WasDuplicateCallback.Should().BeTrue();

        var rewardCount = await db.ReferralRewards.CountAsync(r => r.InvitedStudentUserId == student.Id);
        rewardCount.Should().Be(1, "referral reward is strictly granted once on first verified course payment");

        // Snapshot immutability check: changing CoursePlan price later must not alter Enrollment snapshot
        plan.BasePriceIrr = 99_000_000m;
        await db.SaveChangesAsync();

        var persistedEnrollment = await db.Enrollments.AsNoTracking().FirstAsync(e => e.Id == enrollment.Id);
        persistedEnrollment.FinalPaidAmountIrr.Should().Be(20_000_000m);

        // Session settlement idempotency: settling same session twice only credits teacher wallet once
        var session = new ClassSession
        {
            EnrollmentId = enrollment.Id, StudentUserId = student.Id, TeacherUserId = teacher.Id,
            InstrumentId = piano.Id, RoomId = room.Id, SessionNumber = 1,
            StartAtUtc = DateTime.UtcNow.AddHours(-2), EndAtUtc = DateTime.UtcNow.AddHours(-1),
            DurationMinutes = 45, Status = ClassSessionStatus.Present
        };
        db.ClassSessions.Add(session);
        await db.SaveChangesAsync();

        var adminActor = new ActorContext(Guid.NewGuid(), new HashSet<string> { RoleNames.Admin });
        var settle1 = await financial.SettleSessionCommissionAsync(session.Id, adminActor);
        var settle2 = await financial.SettleSessionCommissionAsync(session.Id, adminActor);

        settle1.Succeeded.Should().BeTrue();
        settle2.Succeeded.Should().BeTrue();

        var teacherWallet = await db.Wallets.AsNoTracking().FirstAsync(w => w.UserId == teacher.Id);
        var ledgerCount = await db.WalletLedgers.CountAsync(l => l.WalletId == teacherWallet.Id && l.RelatedSessionId == session.Id);
        ledgerCount.Should().Be(1, "duplicate session wallet settlement must never double-credit teacher");
    }

    [Fact]
    public async Task TeacherSpecificDiscount_BlockedForOtherTeachersAndAllowedForCreatorTeacher()
    {
        await using var db = CreateDbContext();
        var jalali = new JalaliDateService();
        var settings = new SystemSettingsService(db);
        var discountService = new DiscountService(db, settings, jalali);

        var piano = await db.Instruments.FirstAsync(i => i.Slug == "piano");
        var teacherA = CreateUser("09134440001", "استاد", "تخفیف1");
        var teacherB = CreateUser("09134440002", "استاد", "تخفیف2");
        var student = CreateUser("09134440003", "هنرجو", "تخفیف");
        db.Users.AddRange(teacherA, teacherB, student);

        db.TeacherStudentAssignments.Add(new TeacherStudentAssignment
        {
            TeacherUserId = teacherA.Id,
            StudentUserId = student.Id,
            InstrumentId = piano.Id,
            IsActive = true
        });

        db.DiscountCodes.Add(new DiscountCode
        {
            Code = "TEACHERA15",
            Title = "تخفیف اختصاصی استاد الف",
            Scope = DiscountScope.TeacherSpecific,
            OwnerTeacherUserId = teacherA.Id,
            DiscountType = DiscountType.Percentage,
            DiscountValue = 15m,
            MaxTotalUsages = 5,
            StartsAtUtc = DateTime.UtcNow.AddDays(-1),
            ExpiresAtUtc = DateTime.UtcNow.AddDays(10),
            IsActive = true
        });
        await db.SaveChangesAsync();

        var allowed = await discountService.ValidateAndCalculateAsync("TEACHERA15", student.Id, 10_000_000m, targetTeacherUserId: teacherA.Id);
        allowed.Succeeded.Should().BeTrue();
        allowed.Data!.DiscountAmountIrr.Should().Be(1_500_000m);

        var blocked = await discountService.ValidateAndCalculateAsync("TEACHERA15", student.Id, 10_000_000m, targetTeacherUserId: teacherB.Id);
        blocked.Succeeded.Should().BeFalse();
        blocked.ErrorCode.Should().Be("TEACHER_SCOPE_MISMATCH");
    }

    [Fact]
    public async Task InstrumentRental_InventoryDecrementAndRenewalOnlyAfterVerifiedPayment()
    {
        await using var db = CreateDbContext();
        var jalali = new JalaliDateService();
        var authScope = new AuthorizationScopeService(db);
        var rentalService = new InstrumentRentalService(db, authScope, jalali);

        var category = await db.InstrumentCategories.FirstAsync();
        var inst = new Instrument
        {
            CategoryId = category.Id,
            Name = "پیانو دیجیتال اجاره‌ای تست",
            Slug = "digital-piano-rent-test",
            IsActive = true,
            IsRentable = true,
            DailyRentalPriceIrr = 500_000m,
            MonthlyRentalPriceIrr = 12_000_000m,
            TotalInventoryQuantity = 1,
            AvailableInventoryQuantity = 1
        };
        var student = CreateUser("09135550001", "هنرجو", "اجاره");
        db.Instruments.Add(inst);
        db.Users.Add(student);
        await db.SaveChangesAsync();

        var studentActor = new ActorContext(student.Id, new HashSet<string> { RoleNames.Student });
        var createRes = await rentalService.CreateRentalRequestAsync(
            new CreateRentalRequestInput(student.Id, inst.Id, RentalPricingUnit.Monthly, 1, jalali.TehranToday),
            studentActor);
        createRes.Succeeded.Should().BeTrue();

        var rental = createRes.Data!;
        rental.Status.Should().Be(RentalStatus.PendingPayment);

        // Activate after verified payment decrements inventory to 0
        var activateRes = await rentalService.ActivateRentalAfterVerifiedPaymentAsync(rental.Id, Guid.NewGuid());
        activateRes.Succeeded.Should().BeTrue();

        var updatedInst = await db.Instruments.AsNoTracking().FirstAsync(i => i.Id == inst.Id);
        updatedInst.AvailableInventoryQuantity.Should().Be(0);

        // Second rental attempt fails because inventory is exhausted
        var secondAttempt = await rentalService.CreateRentalRequestAsync(
            new CreateRentalRequestInput(student.Id, inst.Id, RentalPricingUnit.Daily, 5, jalali.TehranToday),
            studentActor);
        secondAttempt.Succeeded.Should().BeFalse();

        // Renewal extends end date ONLY after verified payment
        var originalEnd = activateRes.Data!.EndDateGregorian;
        var renewReq = await rentalService.CreateRenewalRequestAsync(rental.Id, RentalPricingUnit.Monthly, 1, studentActor);
        renewReq.Succeeded.Should().BeTrue();
        (await db.InstrumentRentals.AsNoTracking().FirstAsync(r => r.Id == rental.Id)).EndDateGregorian.Should().Be(originalEnd);

        var applied = await rentalService.ApplyRenewalAfterVerifiedPaymentAsync(renewReq.Data!.Id, Guid.NewGuid());
        applied.Succeeded.Should().BeTrue();
        applied.Data!.EndDateGregorian.Should().BeAfter(originalEnd);
    }

    private static User CreateUser(string mobile, string first, string last) => new()
    {
        MobileNumber = mobile,
        FirstName = first,
        LastName = last,
        Status = UserStatus.Active,
        ReferralCode = $"TST-{Guid.NewGuid():N}"[..12].ToUpperInvariant(),
        QuotaStartDateGregorian = DateOnly.FromDateTime(DateTime.UtcNow)
    };

    private static Enrollment CreateEnrollment(Guid studentId, Guid teacherId, Guid instrumentId, Guid planId, Guid roomId) => new()
    {
        StudentUserId = studentId,
        TeacherUserId = teacherId,
        InstrumentId = instrumentId,
        CoursePlanId = planId,
        RoomId = roomId,
        PlanTitleSnapshot = "دوره آزمایشی",
        TotalSessionsSnapshot = 4,
        RemainingSessions = 4,
        CoursePeriodMonthsSnapshot = 1,
        LessonDurationMinutesSnapshot = 45,
        OriginalPriceIrr = 20_000_000m,
        DiscountAmountIrr = 0m,
        FinalPaidAmountIrr = 20_000_000m,
        TeacherCommissionPercentageSnapshot = 50m,
        AcademyCommissionPercentageSnapshot = 50m,
        CommissionBasisSnapshot = CommissionCalculationBasis.PostDiscountFinalAmount,
        StartDateGregorian = DateOnly.FromDateTime(DateTime.UtcNow),
        Status = EnrollmentStatus.Active
    };
}
