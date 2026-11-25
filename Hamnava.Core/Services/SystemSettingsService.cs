using System.Globalization;
using Hamnava.DataLayer.Context;
using Hamnava.Domain.Entities.Cms;
using Hamnava.Domain.Entities.Finance;
using Microsoft.EntityFrameworkCore;

namespace Hamnava.Core.Services;

public interface ISystemSettingsService
{
    Task<string> GetStringAsync(string key, string fallback = "", CancellationToken ct = default);
    Task<int> GetIntAsync(string key, int fallback, CancellationToken ct = default);
    Task<decimal> GetDecimalAsync(string key, decimal fallback, CancellationToken ct = default);
    Task<bool> GetBoolAsync(string key, bool fallback, CancellationToken ct = default);
    Task<IReadOnlyList<int>> GetIntListAsync(string key, IReadOnlyList<int> fallback, CancellationToken ct = default);
    Task<FinancialSetting> GetCurrentFinancialSettingAsync(CancellationToken ct = default);
    Task<IReadOnlyList<SystemSetting>> GetAllEditableAsync(CancellationToken ct = default);
    Task UpdateSettingAsync(string key, string newValue, Guid actorUserId, CancellationToken ct = default);
}

public sealed class SystemSettingsService : ISystemSettingsService
{
    private readonly HamnavaDbContext _db;

    public SystemSettingsService(HamnavaDbContext db)
    {
        _db = db;
    }

    public async Task<string> GetStringAsync(string key, string fallback = "", CancellationToken ct = default)
    {
        var setting = await _db.SystemSettings
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Key == key, ct);
        return setting?.Value ?? fallback;
    }

    public async Task<int> GetIntAsync(string key, int fallback, CancellationToken ct = default)
    {
        var raw = await GetStringAsync(key, string.Empty, ct);
        return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : fallback;
    }

    public async Task<decimal> GetDecimalAsync(string key, decimal fallback, CancellationToken ct = default)
    {
        var raw = await GetStringAsync(key, string.Empty, ct);
        return decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : fallback;
    }

    public async Task<bool> GetBoolAsync(string key, bool fallback, CancellationToken ct = default)
    {
        var raw = await GetStringAsync(key, string.Empty, ct);
        return bool.TryParse(raw, out var parsed) ? parsed : fallback;
    }

    public async Task<IReadOnlyList<int>> GetIntListAsync(string key, IReadOnlyList<int> fallback, CancellationToken ct = default)
    {
        var raw = await GetStringAsync(key, string.Empty, ct);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return fallback;
        }

        var list = raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? (int?)v : null)
            .Where(v => v.HasValue && v.Value > 0)
            .Select(v => v!.Value)
            .Distinct()
            .ToList();

        return list.Count > 0 ? list : fallback;
    }

    public async Task<FinancialSetting> GetCurrentFinancialSettingAsync(CancellationToken ct = default)
    {
        var setting = await _db.FinancialSettings
            .AsNoTracking()
            .OrderByDescending(f => f.CreatedAtUtc)
            .FirstOrDefaultAsync(f => f.IsCurrent, ct);

        return setting ?? new FinancialSetting();
    }

    public async Task<IReadOnlyList<SystemSetting>> GetAllEditableAsync(CancellationToken ct = default) =>
        await _db.SystemSettings
            .AsNoTracking()
            .Where(s => s.IsEditableInAdminPanel)
            .OrderBy(s => s.Category)
            .ThenBy(s => s.Key)
            .ToListAsync(ct);

    public async Task UpdateSettingAsync(string key, string newValue, Guid actorUserId, CancellationToken ct = default)
    {
        var setting = await _db.SystemSettings.FirstOrDefaultAsync(s => s.Key == key, ct)
            ?? throw new KeyNotFoundException($"تنظیم '{key}' یافت نشد.");

        if (!setting.IsEditableInAdminPanel)
        {
            throw new InvalidOperationException("این تنظیم قابل ویرایش نیست.");
        }

        setting.Value = newValue.Trim();
        setting.UpdatedAtUtc = System.DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
    }
}
