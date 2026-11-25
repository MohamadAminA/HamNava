using Hamnava.Domain.Entities.Identity;
using Hamnava.Domain.Enums;

namespace Hamnava.Domain.Entities.Instruments;

public class InstrumentCategory : AuditableSoftDeleteEntity
{
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
    public int DisplayOrder { get; set; }

    public ICollection<Instrument> Instruments { get; set; } = new List<Instrument>();
}

public class Instrument : AuditableSoftDeleteEntity
{
    public Guid CategoryId { get; set; }
    public InstrumentCategory Category { get; set; } = null!;

    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? ImagePath { get; set; }

    public bool IsRentable { get; set; }
    public decimal DailyRentalPriceIrr { get; set; }
    public decimal MonthlyRentalPriceIrr { get; set; }

    /// <summary>Total physical units owned by the academy.</summary>
    public int TotalInventoryQuantity { get; set; }

    /// <summary>Currently available units, protected by RowVersion concurrency token and SQL transactions.</summary>
    public int AvailableInventoryQuantity { get; set; }

    public bool IsActive { get; set; } = true;
    public int DisplayOrder { get; set; }

    public ICollection<RoomSupportedInstrument> SupportedRooms { get; set; } = new List<RoomSupportedInstrument>();
    public ICollection<InstrumentRental> Rentals { get; set; } = new List<InstrumentRental>();
    public ICollection<TeacherInstrument> Teachers { get; set; } = new List<TeacherInstrument>();
}

public class InstrumentRental : AuditableSoftDeleteEntity
{
    public Guid StudentUserId { get; set; }
    public User Student { get; set; } = null!;

    public Guid InstrumentId { get; set; }
    public Instrument Instrument { get; set; } = null!;

    public Guid? RequestedByUserId { get; set; }
    public User? RequestedByUser { get; set; }

    public RentalPricingUnit PricingUnit { get; set; } = RentalPricingUnit.Monthly;
    public int DurationUnits { get; set; } = 1;

    /// <summary>Snapshot of unit price at booking time (IRR).</summary>
    public decimal UnitPriceSnapshotIrr { get; set; }
    public decimal OriginalAmountIrr { get; set; }
    public decimal DiscountAmountIrr { get; set; }
    public decimal FinalPayableAmountIrr { get; set; }

    public DateOnly StartDateGregorian { get; set; }
    public DateOnly EndDateGregorian { get; set; }
    public DateOnly? ReturnedDateGregorian { get; set; }

    public RentalStatus Status { get; set; } = RentalStatus.PendingPayment;
    public Guid? PaymentId { get; set; }
    public string? Notes { get; set; }

    public ICollection<InstrumentRentalRenewal> Renewals { get; set; } = new List<InstrumentRentalRenewal>();
}

public class InstrumentRentalRenewal : BaseEntity
{
    public Guid InstrumentRentalId { get; set; }
    public InstrumentRental Rental { get; set; } = null!;

    public RentalPricingUnit PricingUnit { get; set; } = RentalPricingUnit.Monthly;
    public int AddedDurationUnits { get; set; } = 1;
    public DateOnly PreviousEndDateGregorian { get; set; }
    public DateOnly NewEndDateGregorian { get; set; }
    public decimal AmountIrr { get; set; }
    public Guid? PaymentId { get; set; }
    public bool IsApplied { get; set; }
    public DateTime? AppliedAtUtc { get; set; }
}

public class Room : AuditableSoftDeleteEntity
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }

    /// <summary>Database-driven capacity (initial seed = 2, never hardcoded in scheduling engine).</summary>
    public int Capacity { get; set; } = 2;

    public bool IsActive { get; set; } = true;
    public int DisplayOrder { get; set; }

    public ICollection<RoomSupportedInstrument> SupportedInstruments { get; set; } = new List<RoomSupportedInstrument>();
}

public class RoomSupportedInstrument : BaseEntity
{
    public Guid RoomId { get; set; }
    public Room Room { get; set; } = null!;

    public Guid InstrumentId { get; set; }
    public Instrument Instrument { get; set; } = null!;
}
