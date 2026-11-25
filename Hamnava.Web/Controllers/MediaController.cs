using Hamnava.Core.Media;
using Hamnava.Core.Security;
using Hamnava.DataLayer.Context;
using Hamnava.Domain.Enums;
using Hamnava.Web.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Hamnava.Web.Controllers;

/// <summary>
/// Authorized media delivery endpoint supporting HTTP Range streaming for compressed videos.
/// Never exposes raw filesystem paths; strictly validates student visibility consent & role scope.
/// </summary>
[Route("media")]
public sealed class MediaController : Controller
{
    private readonly HamnavaDbContext _db;
    private readonly IMediaStorageProvider _storage;
    private readonly IAuthorizationScopeService _authScope;

    public MediaController(
        HamnavaDbContext db,
        IMediaStorageProvider storage,
        IAuthorizationScopeService authScope)
    {
        _db = db;
        _storage = storage;
        _authScope = authScope;
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> StreamMedia(Guid id, [FromQuery] bool download = false, CancellationToken ct = default)
    {
        var media = await _db.PortfolioMedias
            .AsNoTracking()
            .Include(m => m.Student)
            .FirstOrDefaultAsync(m => m.Id == id, ct);

        if (media is null)
        {
            return NotFound();
        }

        var hasConsent = await HasPublicMediaConsentAsync(media.StudentUserId, ct);
        if (!await CanReadMediaAsync(media.StudentUserId, media.IsPubliclyVisible && media.Student.IsPublicProfileVisible && hasConsent, ct))
        {
            return Forbid();
        }

        var key = !string.IsNullOrWhiteSpace(media.OptimizedStorageKey)
            ? media.OptimizedStorageKey
            : media.OriginalStorageKey;

        if (string.IsNullOrWhiteSpace(key))
        {
            return NotFound();
        }

        var stream = await _storage.OpenReadAsync(key, ct);
        if (stream is null)
        {
            return NotFound();
        }

        var contentType = media.MediaType == MediaType.Video ? "video/mp4" : "image/webp";

        if (download && media.AllowDownload)
        {
            var ext = media.MediaType == MediaType.Video ? ".mp4" : ".webp";
            return File(stream, contentType, $"hamnava-{media.Id:N}{ext}", enableRangeProcessing: true);
        }

        return File(stream, contentType, enableRangeProcessing: true);
    }

    [HttpGet("{id:guid}/thumb")]
    public async Task<IActionResult> StreamThumbnail(Guid id, CancellationToken ct = default)
    {
        var media = await _db.PortfolioMedias
            .AsNoTracking()
            .Include(m => m.Student)
            .FirstOrDefaultAsync(m => m.Id == id, ct);

        if (media is null || string.IsNullOrWhiteSpace(media.ThumbnailStorageKey))
        {
            return NotFound();
        }

        var hasConsent = await HasPublicMediaConsentAsync(media.StudentUserId, ct);
        if (!await CanReadMediaAsync(media.StudentUserId, media.IsPubliclyVisible && media.Student.IsPublicProfileVisible && hasConsent, ct))
        {
            return Forbid();
        }

        var stream = await _storage.OpenReadAsync(media.ThumbnailStorageKey, ct);
        if (stream is null)
        {
            return NotFound();
        }

        return File(stream, "image/webp");
    }

    private async Task<bool> HasPublicMediaConsentAsync(Guid studentUserId, CancellationToken ct)
    {
        var consent = await _db.MediaConsents
            .AsNoTracking()
            .OrderByDescending(c => c.ConsentedAtUtc)
            .FirstOrDefaultAsync(c => c.StudentUserId == studentUserId, ct);
        return consent is null || consent.AllowPublicPortfolio;
    }

    private async Task<bool> CanReadMediaAsync(Guid studentUserId, bool isPubliclyVisible, CancellationToken ct)
    {
        if (isPubliclyVisible)
        {
            return true;
        }

        if (User.Identity?.IsAuthenticated != true)
        {
            return false;
        }

        var actor = HttpContext.ToActorContext();
        return await _authScope.CanAccessStudentAsync(actor, studentUserId, ct);
    }
}
