using System.Text.Json;
using Hamnava.DataLayer.Context;
using Hamnava.Domain.Constants;
using Hamnava.Domain.Entities.Audit;
using Microsoft.EntityFrameworkCore;

namespace Hamnava.Core.Security;

public sealed record ActorContext(
    Guid UserId,
    IReadOnlySet<string> Roles,
    string? IpAddress = null,
    string? UserAgent = null,
    string? CorrelationId = null)
{
    public bool IsAdmin => Roles.Contains(RoleNames.Admin);
    public bool IsTeacher => Roles.Contains(RoleNames.Teacher);
    public bool IsStudent => Roles.Contains(RoleNames.Student);
    public bool IsReferrer => Roles.Contains(RoleNames.Referrer);
}

public interface IAuthorizationScopeService
{
    Task<bool> CanAccessStudentAsync(ActorContext actor, Guid targetStudentUserId, CancellationToken ct = default);
    Task EnsureCanAccessStudentAsync(ActorContext actor, Guid targetStudentUserId, CancellationToken ct = default);
    Task<bool> CanManageTeacherResourceAsync(ActorContext actor, Guid teacherUserId, CancellationToken ct = default);
    Task EnsureCanManageTeacherResourceAsync(ActorContext actor, Guid teacherUserId, CancellationToken ct = default);
    Task<IReadOnlyList<Guid>> GetAccessibleStudentIdsForTeacherAsync(Guid teacherUserId, CancellationToken ct = default);
    Task WriteAuditLogAsync(
        ActorContext? actor,
        string action,
        string entityName,
        string? entityId,
        object? beforeState = null,
        object? afterState = null,
        CancellationToken ct = default);
}

/// <summary>
/// Centralized server-side authorization & horizontal privilege escalation guard.
/// Admin bypasses teacher ownership restrictions; Teachers are strictly restricted
/// to their active assigned students; Students can access only their own records.
/// </summary>
public sealed class AuthorizationScopeService : IAuthorizationScopeService
{
    private readonly HamnavaDbContext _db;

    public AuthorizationScopeService(HamnavaDbContext db)
    {
        _db = db;
    }

    public async Task<bool> CanAccessStudentAsync(ActorContext actor, Guid targetStudentUserId, CancellationToken ct = default)
    {
        if (actor.IsAdmin)
        {
            return true;
        }

        if (actor.UserId == targetStudentUserId)
        {
            return true;
        }

        if (actor.IsTeacher)
        {
            return await _db.TeacherStudentAssignments
                .AsNoTracking()
                .AnyAsync(a =>
                    a.TeacherUserId == actor.UserId &&
                    a.StudentUserId == targetStudentUserId &&
                    a.IsActive, ct);
        }

        return false;
    }

    public async Task EnsureCanAccessStudentAsync(ActorContext actor, Guid targetStudentUserId, CancellationToken ct = default)
    {
        if (!await CanAccessStudentAsync(actor, targetStudentUserId, ct))
        {
            throw new UnauthorizedAccessException("شما مجوز دسترسی به اطلاعات این هنرجو را ندارید.");
        }
    }

    public Task<bool> CanManageTeacherResourceAsync(ActorContext actor, Guid teacherUserId, CancellationToken ct = default)
    {
        if (actor.IsAdmin)
        {
            return Task.FromResult(true);
        }

        return Task.FromResult(actor.IsTeacher && actor.UserId == teacherUserId);
    }

    public async Task EnsureCanManageTeacherResourceAsync(ActorContext actor, Guid teacherUserId, CancellationToken ct = default)
    {
        if (!await CanManageTeacherResourceAsync(actor, teacherUserId, ct))
        {
            throw new UnauthorizedAccessException("شما مجوز دسترسی به این بخش را ندارید.");
        }
    }

    public async Task<IReadOnlyList<Guid>> GetAccessibleStudentIdsForTeacherAsync(Guid teacherUserId, CancellationToken ct = default) =>
        await _db.TeacherStudentAssignments
            .AsNoTracking()
            .Where(a => a.TeacherUserId == teacherUserId && a.IsActive)
            .Select(a => a.StudentUserId)
            .Distinct()
            .ToListAsync(ct);

    public async Task WriteAuditLogAsync(
        ActorContext? actor,
        string action,
        string entityName,
        string? entityId,
        object? beforeState = null,
        object? afterState = null,
        CancellationToken ct = default)
    {
        var log = new AuditLog
        {
            ActorUserId = actor?.UserId,
            ActorRole = actor is null ? null : string.Join(",", actor.Roles),
            Action = action,
            EntityName = entityName,
            EntityId = entityId,
            BeforeStateJson = beforeState is null ? null : JsonSerializer.Serialize(beforeState),
            AfterStateJson = afterState is null ? null : JsonSerializer.Serialize(afterState),
            CorrelationId = actor?.CorrelationId,
            IpAddress = actor?.IpAddress,
            UserAgent = actor?.UserAgent,
            OccurredAtUtc = System.DateTime.UtcNow
        };

        _db.AuditLogs.Add(log);
        await _db.SaveChangesAsync(ct);
    }
}
