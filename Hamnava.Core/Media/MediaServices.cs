using System.Diagnostics;
using Hamnava.Core.Common;
using Hamnava.Core.Security;
using Hamnava.Core.Services;
using Hamnava.DataLayer.Context;
using Hamnava.Domain.Constants;
using Hamnava.Domain.Entities.Media;
using Hamnava.Domain.Enums;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SkiaSharp;

namespace Hamnava.Core.Media;

public sealed class MediaStorageOptions
{
    public const string SectionName = "MediaStorage";
    public string RootPath { get; set; } = "/var/lib/hamnava/media";
    public string TempPath { get; set; } = "/var/lib/hamnava/temp";
    public string FfmpegExecutablePath { get; set; } = "/usr/bin/ffmpeg";
    public string FfprobeExecutablePath { get; set; } = "/usr/bin/ffprobe";
}

public interface IMediaStorageProvider
{
    Task<string> SaveStreamAsync(Stream content, string relativeFolder, string safeExtension, CancellationToken ct = default);
    string ResolveSafePhysicalPath(string storageKey);
    Task<Stream?> OpenReadAsync(string storageKey, CancellationToken ct = default);
    Task DeleteIfExistsAsync(string storageKey, CancellationToken ct = default);
}

/// <summary>
/// Local filesystem storage provider with strict path-traversal prevention.
/// Ready to be swapped with S3/MinIO object storage without touching business logic.
/// </summary>
public sealed class LocalMediaStorageProvider : IMediaStorageProvider
{
    private readonly string _normalizedRoot;

    public LocalMediaStorageProvider(IOptions<MediaStorageOptions> options)
    {
        var root = string.IsNullOrWhiteSpace(options.Value.RootPath)
            ? Path.Combine(AppContext.BaseDirectory, "storage", "media")
            : options.Value.RootPath;
        _normalizedRoot = Path.GetFullPath(root);
        Directory.CreateDirectory(_normalizedRoot);
    }

    public async Task<string> SaveStreamAsync(Stream content, string relativeFolder, string safeExtension, CancellationToken ct = default)
    {
        var cleanFolder = relativeFolder.Replace("..", string.Empty, StringComparison.Ordinal).Trim('/', '\\');
        var cleanExt = safeExtension.StartsWith('.') ? safeExtension.ToLowerInvariant() : "." + safeExtension.ToLowerInvariant();
        var fileName = $"{Guid.NewGuid():N}{cleanExt}";
        var storageKey = Path.Combine(cleanFolder, fileName).Replace('\\', '/');
        var physicalPath = ResolveSafePhysicalPath(storageKey);

        Directory.CreateDirectory(Path.GetDirectoryName(physicalPath)!);
        await using var fs = new FileStream(physicalPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true);
        await content.CopyToAsync(fs, ct);

        return storageKey;
    }

    public string ResolveSafePhysicalPath(string storageKey)
    {
        if (string.IsNullOrWhiteSpace(storageKey) || storageKey.Contains("..", StringComparison.Ordinal))
        {
            throw new UnauthorizedAccessException("مسیر فایل نامعتبر است.");
        }

        var combined = Path.GetFullPath(Path.Combine(_normalizedRoot, storageKey.TrimStart('/', '\\')));
        if (!combined.StartsWith(_normalizedRoot, StringComparison.Ordinal))
        {
            throw new UnauthorizedAccessException("تلاش برای خروج از مسیر مجاز ذخیره‌سازی مسدود شد.");
        }

        return combined;
    }

    public Task<Stream?> OpenReadAsync(string storageKey, CancellationToken ct = default)
    {
        var path = ResolveSafePhysicalPath(storageKey);
        if (!File.Exists(path))
        {
            return Task.FromResult<Stream?>(null);
        }

        Stream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
        return Task.FromResult<Stream?>(stream);
    }

    public Task DeleteIfExistsAsync(string storageKey, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(storageKey))
        {
            return Task.CompletedTask;
        }

        var path = ResolveSafePhysicalPath(storageKey);
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        return Task.CompletedTask;
    }
}

/// <summary>
/// Magic-byte file signature validator so untrusted extensions can never upload executables or spoofed MIME types.
/// </summary>
public static class MediaSignatureValidator
{
    public static bool ValidateMagicBytes(ReadOnlySpan<byte> header, string extension) =>
        extension.ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => header.Length >= 3 && header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF,
            ".png" => header.Length >= 8 && header[0] == 0x89 && header[1] == 0x50 && header[2] == 0x4E && header[3] == 0x47,
            ".webp" => header.Length >= 12 && header[0] == (byte)'R' && header[1] == (byte)'I' && header[2] == (byte)'F' && header[3] == (byte)'F'
                       && header[8] == (byte)'W' && header[9] == (byte)'E' && header[10] == (byte)'B' && header[11] == (byte)'P',
            ".mp4" or ".mov" => header.Length >= 8 && header[4] == (byte)'f' && header[5] == (byte)'t' && header[6] == (byte)'y' && header[7] == (byte)'p',
            _ => false
        };
}

public interface IMediaProcessingService
{
    Task<ServiceResult<PortfolioMedia>> UploadPortfolioMediaAsync(
        Guid studentUserId,
        string title,
        string? description,
        string originalFileName,
        string contentType,
        long fileSizeBytes,
        Stream fileStream,
        bool requestPublicVisibility,
        ActorContext actor,
        CancellationToken ct = default);

    Task ProcessMediaJobAsync(Guid portfolioMediaId, CancellationToken ct = default);
}

public sealed class MediaProcessingService : IMediaProcessingService
{
    private readonly HamnavaDbContext _db;
    private readonly IMediaStorageProvider _storage;
    private readonly IAuthorizationScopeService _authScope;
    private readonly ISystemSettingsService _settings;
    private readonly IBackgroundJobClient _backgroundJobs;
    private readonly MediaStorageOptions _options;
    private readonly ILogger<MediaProcessingService> _logger;

    public MediaProcessingService(
        HamnavaDbContext db,
        IMediaStorageProvider storage,
        IAuthorizationScopeService authScope,
        ISystemSettingsService settings,
        IBackgroundJobClient backgroundJobs,
        IOptions<MediaStorageOptions> options,
        ILogger<MediaProcessingService> logger)
    {
        _db = db;
        _storage = storage;
        _authScope = authScope;
        _settings = settings;
        _backgroundJobs = backgroundJobs;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<ServiceResult<PortfolioMedia>> UploadPortfolioMediaAsync(
        Guid studentUserId,
        string title,
        string? description,
        string originalFileName,
        string contentType,
        long fileSizeBytes,
        Stream fileStream,
        bool requestPublicVisibility,
        ActorContext actor,
        CancellationToken ct = default)
    {
        await _authScope.EnsureCanAccessStudentAsync(actor, studentUserId, ct);

        var ext = Path.GetExtension(originalFileName).ToLowerInvariant();
        var allowedImages = (await _settings.GetStringAsync(SystemSettingKeys.MediaAllowedImageExtensions, ".jpg,.jpeg,.png,.webp", ct))
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var allowedVideos = (await _settings.GetStringAsync(SystemSettingKeys.MediaAllowedVideoExtensions, ".mp4,.mov", ct))
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        MediaType mediaType;
        long maxBytes;
        if (allowedImages.Contains(ext, StringComparer.OrdinalIgnoreCase))
        {
            mediaType = MediaType.Image;
            maxBytes = await _settings.GetIntAsync(SystemSettingKeys.MediaMaxImageSizeMb, 10, ct) * 1024L * 1024L;
        }
        else if (allowedVideos.Contains(ext, StringComparer.OrdinalIgnoreCase))
        {
            mediaType = MediaType.Video;
            maxBytes = await _settings.GetIntAsync(SystemSettingKeys.MediaMaxVideoSizeMb, 250, ct) * 1024L * 1024L;
        }
        else
        {
            return ServiceResult<PortfolioMedia>.Fail("فرمت فایل انتخاب‌شده مجاز نیست.", "UNSUPPORTED_MEDIA_FORMAT");
        }

        if (fileSizeBytes <= 0 || fileSizeBytes > maxBytes)
        {
            return ServiceResult<PortfolioMedia>.Fail("حجم فایل بیشتر از سقف مجاز است.", "FILE_TOO_LARGE");
        }

        // Enforce student privacy consent before marking public visibility
        var allowPublic = false;
        if (requestPublicVisibility)
        {
            var consent = await _db.MediaConsents.AsNoTracking().FirstOrDefaultAsync(c => c.StudentUserId == studentUserId, ct);
            allowPublic = consent is not null &&
                          consent.AllowPublicPortfolio &&
                          (mediaType == MediaType.Video ? consent.AllowPublicVideos : consent.AllowPublicImages);
        }

        // Validate real magic bytes before persisting
        var header = new byte[16];
        var read = await fileStream.ReadAsync(header.AsMemory(0, header.Length), ct);
        if (!MediaSignatureValidator.ValidateMagicBytes(header.AsSpan(0, read), ext))
        {
            return ServiceResult<PortfolioMedia>.Fail("محتوای فایل با پسوند آن همخوانی ندارد یا نامعتبر است.", "INVALID_FILE_SIGNATURE");
        }

        if (fileStream.CanSeek)
        {
            fileStream.Position = 0;
        }

        var folder = $"students/{studentUserId:N}/{(mediaType == MediaType.Video ? "videos" : "images")}/original";
        var originalKey = await _storage.SaveStreamAsync(fileStream, folder, ext, ct);

        var media = new PortfolioMedia
        {
            StudentUserId = studentUserId,
            UploadedByUserId = actor.UserId,
            Title = title.Trim(),
            Description = description?.Trim(),
            MediaType = mediaType,
            ProcessingStatus = MediaProcessingStatus.Uploaded,
            OriginalStorageKey = originalKey,
            OriginalFileNameSanitized = Path.GetFileName(originalFileName),
            ContentType = contentType,
            OriginalFileSizeBytes = fileSizeBytes,
            IsPubliclyVisible = allowPublic,
            CreatedByUserId = actor.UserId
        };

        var jobRecord = new MediaProcessingJob
        {
            PortfolioMedia = media,
            Status = MediaProcessingStatus.Pending
        };

        _db.PortfolioMedias.Add(media);
        _db.MediaProcessingJobs.Add(jobRecord);
        await _db.SaveChangesAsync(ct);

        var hangfireJobId = _backgroundJobs.Enqueue<IMediaProcessingService>(s => s.ProcessMediaJobAsync(media.Id, CancellationToken.None));
        jobRecord.HangfireJobId = hangfireJobId;
        await _db.SaveChangesAsync(ct);

        return ServiceResult<PortfolioMedia>.Ok(media, "فایل با موفقیت آپلود شد و در صف پردازش قرار گرفت.");
    }

    public async Task ProcessMediaJobAsync(Guid portfolioMediaId, CancellationToken ct = default)
    {
        var media = await _db.PortfolioMedias
            .Include(m => m.ProcessingJobs)
            .FirstOrDefaultAsync(m => m.Id == portfolioMediaId, ct);

        if (media is null)
        {
            return;
        }

        var job = media.ProcessingJobs.OrderByDescending(j => j.CreatedAtUtc).FirstOrDefault();
        media.ProcessingStatus = MediaProcessingStatus.Processing;
        if (job is not null)
        {
            job.Status = MediaProcessingStatus.Processing;
            job.StartedAtUtc = System.DateTime.UtcNow;
            job.AttemptCount++;
        }

        await _db.SaveChangesAsync(ct);

        try
        {
            if (media.MediaType == MediaType.Image)
            {
                await OptimizeImageAsync(media, ct);
            }
            else
            {
                await TranscodeVideoWithFfmpegAsync(media, ct);
            }

            media.ProcessingStatus = MediaProcessingStatus.Completed;
            if (job is not null)
            {
                job.Status = MediaProcessingStatus.Completed;
                job.ProgressPercentage = 100;
                job.CompletedAtUtc = System.DateTime.UtcNow;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Media processing failed for PortfolioMedia {MediaId}", portfolioMediaId);
            media.ProcessingStatus = MediaProcessingStatus.Failed;
            if (job is not null)
            {
                job.Status = MediaProcessingStatus.Failed;
                job.ErrorMessage = ex.Message;
                job.CompletedAtUtc = System.DateTime.UtcNow;
            }
        }

        await _db.SaveChangesAsync(ct);
    }

    private async Task OptimizeImageAsync(PortfolioMedia media, CancellationToken ct)
    {
        var inputPath = _storage.ResolveSafePhysicalPath(media.OriginalStorageKey);
        using var bitmap = SKBitmap.Decode(inputPath) ?? throw new InvalidOperationException("تصویر قابل خواندن نیست.");

        media.WidthPx = bitmap.Width;
        media.HeightPx = bitmap.Height;

        var convertToWebP = await _settings.GetBoolAsync(SystemSettingKeys.MediaConvertImagesToWebP, true, ct);
        var format = convertToWebP ? SKEncodedImageFormat.Webp : SKEncodedImageFormat.Jpeg;
        var ext = convertToWebP ? ".webp" : ".jpg";

        // Generate thumbnail (max 480px width)
        var thumbWidth = Math.Min(480, bitmap.Width);
        var thumbHeight = (int)Math.Round(bitmap.Height * ((double)thumbWidth / bitmap.Width));
        using var thumbBitmap = bitmap.Resize(new SKImageInfo(thumbWidth, thumbHeight), SKSamplingOptions.Default);
        using var thumbImage = SKImage.FromBitmap(thumbBitmap);
        using var thumbData = thumbImage.Encode(format, 82);
        await using var thumbStream = thumbData.AsStream();
        media.ThumbnailStorageKey = await _storage.SaveStreamAsync(thumbStream, $"students/{media.StudentUserId:N}/images/thumbs", ext, ct);

        // Generate optimized full image (max 1920px width, strips EXIF)
        var optWidth = Math.Min(1920, bitmap.Width);
        var optHeight = (int)Math.Round(bitmap.Height * ((double)optWidth / bitmap.Width));
        using var optBitmap = bitmap.Resize(new SKImageInfo(optWidth, optHeight), SKSamplingOptions.Default);
        using var optImage = SKImage.FromBitmap(optBitmap);
        using var optData = optImage.Encode(format, 85);
        await using var optStream = optData.AsStream();
        media.OptimizedStorageKey = await _storage.SaveStreamAsync(optStream, $"students/{media.StudentUserId:N}/images/optimized", ext, ct);
        media.OptimizedFileSizeBytes = optData.Size;
    }

    private async Task TranscodeVideoWithFfmpegAsync(PortfolioMedia media, CancellationToken ct)
    {
        if (!File.Exists(_options.FfmpegExecutablePath))
        {
            throw new FileNotFoundException($"FFmpeg executable not found at '{_options.FfmpegExecutablePath}'.");
        }

        var inputPath = _storage.ResolveSafePhysicalPath(media.OriginalStorageKey);
        var optimizedKey = $"students/{media.StudentUserId:N}/videos/optimized/{Guid.NewGuid():N}.mp4";
        var thumbKey = $"students/{media.StudentUserId:N}/videos/thumbs/{Guid.NewGuid():N}.jpg";

        var outputPath = _storage.ResolveSafePhysicalPath(optimizedKey);
        var thumbPath = _storage.ResolveSafePhysicalPath(thumbKey);
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        Directory.CreateDirectory(Path.GetDirectoryName(thumbPath)!);

        // Web-compatible H.264/AAC MP4 with faststart moov atom for instant mobile streaming
        await RunProcessAsync(
            _options.FfmpegExecutablePath,
            $"-y -i \"{inputPath}\" -c:v libx264 -preset medium -crf 24 -vf \"scale='min(1280,iw)':-2\" -c:a aac -b:a 128k -movflags +faststart \"{outputPath}\"",
            ct);

        // Extract poster thumbnail at 1 second
        await RunProcessAsync(
            _options.FfmpegExecutablePath,
            $"-y -ss 00:00:01 -i \"{outputPath}\" -vframes 1 -q:v 3 \"{thumbPath}\"",
            ct);

        media.OptimizedStorageKey = optimizedKey;
        media.ThumbnailStorageKey = thumbKey;
        media.OptimizedFileSizeBytes = new FileInfo(outputPath).Length;
    }

    private static async Task RunProcessAsync(string fileName, string arguments, CancellationToken ct)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        process.Start();
        var stdErrTask = process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);
        var stdErr = await stdErrTask;

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"FFmpeg exited with code {process.ExitCode}: {stdErr[..Math.Min(stdErr.Length, 400)]}");
        }
    }
}
