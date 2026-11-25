using System.Data;
using Hamnava.Core.Common;
using Hamnava.Core.DateTime;
using Hamnava.Core.Security;
using Hamnava.DataLayer.Context;
using Hamnava.Domain.Entities.Education;
using Hamnava.Domain.Entities.Instruments;
using Hamnava.Domain.Enums;
using Hamnava.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace Hamnava.Core.Services;

public sealed record CreateRentalRequestInput(
    Guid StudentUserId,
    Guid InstrumentId,
    RentalPricingUnit PricingUnit,
    int DurationUnits,
    DateOnly StartDateGregorian,
    string? Notes = null);

public interface IInstrumentRentalService
{
    Task<ServiceResult<InstrumentRental>> CreateRentalRequestAsync(
        CreateRentalRequestInput input,
        ActorContext actor,
        CancellationToken ct = default);
    Task<ServiceResult<InstrumentRental>> ActivateRentalAfterVerifiedPaymentAsync(
        Guid rentalId,
        Guid paymentId,
        CancellationToken ct = default);
    Task<ServiceResult<InstrumentRentalRenewal>> CreateRenewalRequestAsync(
        Guid rentalId,
        RentalPricingUnit pricingUnit,
        int addedUnits,
        ActorContext actor,
        CancellationToken ct = default);
    Task<ServiceResult<InstrumentRental>> ApplyRenewalAfterVerifiedPaymentAsync(
        Guid renewalId,
        Guid paymentId,
        CancellationToken ct = default);
    Task<ServiceResult> MarkRentalReturnedAsync(
        Guid rentalId,
        ActorContext actor,
        CancellationToken ct = default);
}

public sealed class InstrumentRentalService : IInstrumentRentalService
{
    private readonly HamnavaDbContext _db;
    private readonly IAuthorizationScopeService _authScope;
    private readonly IJalaliDateService _jalali;

    public InstrumentRentalService(
        HamnavaDbContext db,
        IAuthorizationScopeService authScope,
        IJalaliDateService jalali)
    {
        _db = db;
        _authScope = authScope;
        _jalali = jalali;
    }

    public static decimal CalculateRentalPriceIrr(Instrument instrument, RentalPricingUnit unit, int durationUnits)
    {
        if (durationUnits <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(durationUnits));
        }

        var unitPrice = unit switch
        {
            RentalPricingUnit.Daily => instrument.DailyRentalPriceIrr,
            RentalPricingUnit.Monthly => instrument.MonthlyRentalPriceIrr,
            RentalPricingUnit.CustomDays => instrument.DailyRentalPriceIrr,
            _ => throw new ArgumentOutOfRangeException(nameof(unit))
        };

        return decimal.Round(unitPrice * durationUnits, 0, MidpointRounding.AwayFromZero);
    }

    public async Task<ServiceResult<InstrumentRental>> CreateRentalRequestAsync(
        CreateRentalRequestInput input,
        ActorContext actor,
        CancellationToken ct = default)
    {
        await _authScope.EnsureCanAccessStudentAsync(actor, input.StudentUserId, ct);

        var instrument = await _db.Instruments.FirstOrDefaultAsync(i => i.Id == input.InstrumentId && i.IsActive, ct);
        if (instrument is null || !instrument.IsRentable)
        {
            return ServiceResult<InstrumentRental>.Fail("این ساز قابل اجاره نیست.", "INSTRUMENT_NOT_RENTABLE");
        }

        if (instrument.AvailableInventoryQuantity <= 0)
        {
            return ServiceResult<InstrumentRental>.Fail("در حال حاضر موجودی این ساز برای اجاره به اتمام رسیده است.", "OUT_OF_STOCK");
        }

        var totalIrr = CalculateRentalPriceIrr(instrument, input.PricingUnit, input.DurationUnits);
        var unitPrice = input.PricingUnit == RentalPricingUnit.Monthly
            ? instrument.MonthlyRentalPriceIrr
            : instrument.DailyRentalPriceIrr;

        var endDate = input.PricingUnit == RentalPricingUnit.Monthly
            ? input.StartDateGregorian.AddMonths(input.DurationUnits)
            : input.StartDateGregorian.AddDays(input.DurationUnits);

        var rental = new InstrumentRental
        {
            StudentUserId = input.StudentUserId,
            InstrumentId = instrument.Id,
            RequestedByUserId = actor.UserId,
            PricingUnit = input.PricingUnit,
            DurationUnits = input.DurationUnits,
            UnitPriceSnapshotIrr = unitPrice,
            OriginalAmountIrr = totalIrr,
            DiscountAmountIrr = 0m,
            FinalPayableAmountIrr = totalIrr,
            StartDateGregorian = input.StartDateGregorian,
            EndDateGregorian = endDate,
            Status = RentalStatus.PendingPayment,
            Notes = input.Notes?.Trim()
        };

        _db.InstrumentRentals.Add(rental);
        await _db.SaveChangesAsync(ct);

        return ServiceResult<InstrumentRental>.Ok(rental, "درخواست اجاره ساز ثبت شد. پس از پرداخت آنلاین فعال خواهد شد.");
    }

    public async Task<ServiceResult<InstrumentRental>> ActivateRentalAfterVerifiedPaymentAsync(
        Guid rentalId,
        Guid paymentId,
        CancellationToken ct = default)
    {
        await using var tx = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);

        var rental = await _db.InstrumentRentals
            .Include(r => r.Instrument)
            .FirstOrDefaultAsync(r => r.Id == rentalId, ct);

        if (rental is null)
        {
            return ServiceResult<InstrumentRental>.Fail("رکورد اجاره یافت نشد.", "RENTAL_NOT_FOUND");
        }

        // Idempotent guard
        if (rental.Status is RentalStatus.Active or RentalStatus.Extended)
        {
            return ServiceResult<InstrumentRental>.Ok(rental, "اجاره قبلاً فعال شده است.");
        }

        // Atomic inventory allocation preventing double allocation under concurrent checkouts
        if (rental.Instrument.AvailableInventoryQuantity <= 0)
        {
            await tx.RollbackAsync(ct);
            return ServiceResult<InstrumentRental>.Fail("متاسفانه موجودی این ساز هم‌زمان تکمیل شد. مبلغ قابل بازگشت است.", "INVENTORY_RACE_EXHAUSTED");
        }

        rental.Instrument.AvailableInventoryQuantity--;
        rental.Status = RentalStatus.Active;
        rental.PaymentId = paymentId;

        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return ServiceResult<InstrumentRental>.Ok(rental, "اجاره ساز با موفقیت فعال شد.");
    }

    public async Task<ServiceResult<InstrumentRentalRenewal>> CreateRenewalRequestAsync(
        Guid rentalId,
        RentalPricingUnit pricingUnit,
        int addedUnits,
        ActorContext actor,
        CancellationToken ct = default)
    {
        var rental = await _db.InstrumentRentals
            .Include(r => r.Instrument)
            .FirstOrDefaultAsync(r => r.Id == rentalId, ct);

        if (rental is null)
        {
            return ServiceResult<InstrumentRentalRenewal>.Fail("اجاره موردنظر یافت نشد.", "RENTAL_NOT_FOUND");
        }

        await _authScope.EnsureCanAccessStudentAsync(actor, rental.StudentUserId, ct);

        if (rental.Status is not (RentalStatus.Active or RentalStatus.Extended or RentalStatus.Expired))
        {
            return ServiceResult<InstrumentRentalRenewal>.Fail("تنها اجاره‌های فعال قابل تمدید هستند.", "RENTAL_NOT_RENEWABLE");
        }

        var amountIrr = CalculateRentalPriceIrr(rental.Instrument, pricingUnit, addedUnits);
        var baseEndDate = rental.EndDateGregorian >= _jalali.TehranToday ? rental.EndDateGregorian : _jalali.TehranToday;
        var newEndDate = pricingUnit == RentalPricingUnit.Monthly
            ? baseEndDate.AddMonths(addedUnits)
            : baseEndDate.AddDays(addedUnits);

        var renewal = new InstrumentRentalRenewal
        {
            InstrumentRentalId = rental.Id,
            PricingUnit = pricingUnit,
            AddedDurationUnits = addedUnits,
            PreviousEndDateGregorian = rental.EndDateGregorian,
            NewEndDateGregorian = newEndDate,
            AmountIrr = amountIrr,
            IsApplied = false
        };

        _db.InstrumentRentalRenewals.Add(renewal);
        await _db.SaveChangesAsync(ct);
        return ServiceResult<InstrumentRentalRenewal>.Ok(renewal, "درخواست تمدید اجاره ثبت شد. پس از پرداخت اعمال می‌گردد.");
    }

    public async Task<ServiceResult<InstrumentRental>> ApplyRenewalAfterVerifiedPaymentAsync(
        Guid renewalId,
        Guid paymentId,
        CancellationToken ct = default)
    {
        await using var tx = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);

        var renewal = await _db.InstrumentRentalRenewals
            .Include(r => r.Rental)
            .FirstOrDefaultAsync(r => r.Id == renewalId, ct);

        if (renewal is null)
        {
            return ServiceResult<InstrumentRental>.Fail("درخواست تمدید یافت نشد.", "RENEWAL_NOT_FOUND");
        }

        if (renewal.IsApplied)
        {
            return ServiceResult<InstrumentRental>.Ok(renewal.Rental, "تمدید قبلاً اعمال شده است.");
        }

        renewal.IsApplied = true;
        renewal.AppliedAtUtc = _jalali.UtcNow;
        renewal.PaymentId = paymentId;
        renewal.Rental.EndDateGregorian = renewal.NewEndDateGregorian;
        renewal.Rental.Status = RentalStatus.Extended;

        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return ServiceResult<InstrumentRental>.Ok(renewal.Rental, "مهلت اجاره ساز با موفقیت تمدید شد.");
    }

    public async Task<ServiceResult> MarkRentalReturnedAsync(
        Guid rentalId,
        ActorContext actor,
        CancellationToken ct = default)
    {
        if (!actor.IsAdmin && !actor.IsTeacher)
        {
            return ServiceResult.Fail("مجوز ثبت عودت ساز ندارید.", "FORBIDDEN");
        }

        await using var tx = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);

        var rental = await _db.InstrumentRentals
            .Include(r => r.Instrument)
            .FirstOrDefaultAsync(r => r.Id == rentalId, ct);

        if (rental is null)
        {
            return ServiceResult.Fail("اجاره یافت نشد.", "RENTAL_NOT_FOUND");
        }

        if (rental.Status == RentalStatus.Returned)
        {
            return ServiceResult.Ok("عودت این ساز قبلاً ثبت شده است.");
        }

        rental.Status = RentalStatus.Returned;
        rental.ReturnedDateGregorian = _jalali.TehranToday;
        if (rental.Instrument.AvailableInventoryQuantity < rental.Instrument.TotalInventoryQuantity)
        {
            rental.Instrument.AvailableInventoryQuantity++;
        }

        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return ServiceResult.Ok("عودت ساز ثبت و به موجودی اضافه شد.");
    }
}

public sealed record BookConsultationInput(
    Guid ConsultationPlanId,
    Guid ConsultantUserId,
    System.DateTime StartAtUtc,
    string VisitorFullName,
    string VisitorMobile,
    string? Notes,
    Guid? AuthenticatedUserId = null);

public interface IConsultationService
{
    Task<ServiceResult<ConsultationBooking>> BookConsultationAsync(BookConsultationInput input, CancellationToken ct = default);
    Task<ServiceResult<ConsultationBooking>> ConfirmBookingAfterVerifiedPaymentAsync(Guid bookingId, Guid paymentId, CancellationToken ct = default);
}

public sealed class ConsultationService : IConsultationService
{
    private readonly HamnavaDbContext _db;

    public ConsultationService(HamnavaDbContext db)
    {
        _db = db;
    }

    public async Task<ServiceResult<ConsultationBooking>> BookConsultationAsync(BookConsultationInput input, CancellationToken ct = default)
    {
        if (!MobileNumber.TryParse(input.VisitorMobile, out var mobile) || mobile is null)
        {
            return ServiceResult<ConsultationBooking>.Fail("شماره موبایل معتبر نیست.", "INVALID_MOBILE");
        }

        await using var tx = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);

        var plan = await _db.ConsultationPlans
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == input.ConsultationPlanId && p.IsActive, ct);

        if (plan is null)
        {
            return ServiceResult<ConsultationBooking>.Fail("پلن مشاوره انتخاب‌شده معتبر نیست.", "PLAN_NOT_FOUND");
        }

        var startUtc = System.DateTime.SpecifyKind(input.StartAtUtc, DateTimeKind.Utc);
        var endUtc = startUtc.AddMinutes(plan.DurationMinutes);

        var activeBookingsCount = await _db.ConsultationBookings.CountAsync(b =>
            b.ConsultantUserId == input.ConsultantUserId &&
            b.Status != ConsultationBookingStatus.Canceled &&
            b.Status != ConsultationBookingStatus.Refunded &&
            b.StartAtUtc < endUtc &&
            b.EndAtUtc > startUtc, ct);

        if (activeBookingsCount >= plan.MaxBookingsPerSlot)
        {
            await tx.RollbackAsync(ct);
            return ServiceResult<ConsultationBooking>.Fail("این زمان مشاوره قبلاً رزرو شده است.", "CONSULTATION_SLOT_TAKEN");
        }

        var isFree = plan.PriceIrr <= 0m;
        var booking = new ConsultationBooking
        {
            ConsultationPlanId = plan.Id,
            ConsultantUserId = input.ConsultantUserId,
            UserId = input.AuthenticatedUserId,
            VisitorFullName = input.VisitorFullName.Trim(),
            VisitorMobileNumber = mobile.Value,
            Notes = input.Notes?.Trim(),
            StartAtUtc = startUtc,
            EndAtUtc = endUtc,
            PriceSnapshotIrr = plan.PriceIrr,
            Status = isFree ? ConsultationBookingStatus.Confirmed : ConsultationBookingStatus.PendingPayment
        };

        _db.ConsultationBookings.Add(booking);
        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return ServiceResult<ConsultationBooking>.Ok(
            booking,
            isFree ? "جلسه مشاوره رایگان شما با موفقیت رزرو شد." : "رزرو اولیه ثبت شد؛ برای نهایی‌سازی لطفاً پرداخت را تکمیل کنید.");
    }

    public async Task<ServiceResult<ConsultationBooking>> ConfirmBookingAfterVerifiedPaymentAsync(
        Guid bookingId,
        Guid paymentId,
        CancellationToken ct = default)
    {
        var booking = await _db.ConsultationBookings.FirstOrDefaultAsync(b => b.Id == bookingId, ct);
        if (booking is null)
        {
            return ServiceResult<ConsultationBooking>.Fail("رزرو مشاوره یافت نشد.", "BOOKING_NOT_FOUND");
        }

        if (booking.Status == ConsultationBookingStatus.Confirmed)
        {
            return ServiceResult<ConsultationBooking>.Ok(booking, "مشاوره قبلاً تایید شده است.");
        }

        booking.Status = ConsultationBookingStatus.Confirmed;
        booking.PaymentId = paymentId;
        await _db.SaveChangesAsync(ct);

        return ServiceResult<ConsultationBooking>.Ok(booking, "رزرو مشاوره شما پس از تایید پرداخت نهایی شد.");
    }
}
