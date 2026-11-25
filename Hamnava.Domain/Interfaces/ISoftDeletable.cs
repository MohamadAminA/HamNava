namespace Hamnava.Domain.Interfaces;

/// <summary>
/// Marker contract for entities that must never be physically deleted
/// when educational, financial, or audit history exists.
/// </summary>
public interface ISoftDeletable
{
    bool IsDeleted { get; set; }
    DateTime? DeletedAtUtc { get; set; }
    Guid? DeletedByUserId { get; set; }
}
