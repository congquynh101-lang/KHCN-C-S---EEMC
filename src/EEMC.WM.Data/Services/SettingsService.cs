using System.Text.Json;
using EEMC.WM.Core.Domain;
using EEMC.WM.Core.Settings;
using EEMC.WM.Data.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace EEMC.WM.Data.Services;

/// <summary>Đọc/ghi cấu hình người dùng (lưu trong bảng Settings dưới dạng JSON).</summary>
public sealed class SettingsService
{
    private const string Key = "work-settings";
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };
    private readonly IDbContextFactory<AppDbContext> _factory;

    public SettingsService(IDbContextFactory<AppDbContext> factory) { _factory = factory; }

    public WorkSettings Current { get; private set; } = new();

    public event EventHandler? Changed;

    public async Task LoadAsync()
    {
        await using var db = await _factory.CreateDbContextAsync();
        var row = await db.Settings.AsNoTracking().FirstOrDefaultAsync(s => s.Key == Key);
        if (row == null) { Current = new WorkSettings(); return; }
        try
        {
            Current = JsonSerializer.Deserialize<WorkSettings>(row.Value, Json) ?? new WorkSettings();
            // JSON giữ lại danh sách cũ nhưng trình khởi tạo đã thêm mặc định: tránh nhân đôi
            Current.WorkPeriods = Current.WorkPeriods.DistinctBy(p => (p.Start, p.End)).ToList();
            Current.WorkDays = Current.WorkDays.Distinct().ToList();
            Current.ReminderOffsets = Current.ReminderOffsets.DistinctBy(o => o.Minutes).ToList();
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "Không đọc được cấu hình, dùng cấu hình mặc định.");
            Current = new WorkSettings();
        }
    }

    public async Task SaveAsync(WorkSettings settings)
    {
        var errors = settings.Validate().ToList();
        if (errors.Count > 0) throw new ValidationException(errors);

        bool sharedChanged = settings.SharedFingerprint() != Current.SharedFingerprint();
        await using var db = await _factory.CreateDbContextAsync();
        await WriteAsync(db, Key, JsonSerializer.Serialize(settings, Json));
        if (sharedChanged)
        {
            // Thiết lập làm việc dùng chung đã đổi: đánh dấu để gửi sang các máy khác
            await WriteAsync(db, SharedStampKey, AppDbContext.NextStamp().ToString());
            await WriteAsync(db, SharedDirtyKey, "1");
        }
        await db.SaveChangesAsync();
        Current = settings;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public WorkSettings CloneCurrent() =>
        JsonSerializer.Deserialize<WorkSettings>(JsonSerializer.Serialize(Current, Json), Json)!;

    // ---------------------------------------------------- Đồng bộ thiết lập dùng chung

    private const string SharedStampKey = "sync-shared-settings-stamp";
    private const string SharedDirtyKey = "sync-shared-settings-dirty";

    private static async Task WriteAsync(AppDbContext db, string key, string value)
    {
        var row = await db.Settings.FirstOrDefaultAsync(s => s.Key == key);
        if (row == null) db.Settings.Add(new AppSetting { Key = key, Value = value });
        else row.Value = value;
    }

    /// <returns>(stamp, dirty) của thiết lập dùng chung.</returns>
    public async Task<(long Stamp, bool Dirty)> GetSharedSyncStateAsync()
    {
        await using var db = await _factory.CreateDbContextAsync();
        var stamp = await db.Settings.AsNoTracking().Where(s => s.Key == SharedStampKey).Select(s => s.Value).FirstOrDefaultAsync();
        var dirty = await db.Settings.AsNoTracking().Where(s => s.Key == SharedDirtyKey).Select(s => s.Value).FirstOrDefaultAsync();
        return (long.TryParse(stamp, out var v) ? v : 0, dirty == "1");
    }

    public async Task MarkSharedCleanAsync()
    {
        await using var db = await _factory.CreateDbContextAsync();
        await WriteAsync(db, SharedDirtyKey, "0");
        await db.SaveChangesAsync();
    }

    /// <summary>Áp dụng thiết lập dùng chung nhận từ máy khác nếu mới hơn.</summary>
    public async Task<bool> ApplySharedFromSyncAsync(WorkSettings incoming, long stamp)
    {
        var (localStamp, _) = await GetSharedSyncStateAsync();
        if (stamp <= localStamp) return false;
        var s = CloneCurrent();
        s.CopySharedFrom(incoming);
        if (s.Validate().Any()) return false;
        await using var db = await _factory.CreateDbContextAsync();
        await WriteAsync(db, Key, JsonSerializer.Serialize(s, Json));
        await WriteAsync(db, SharedStampKey, stamp.ToString());
        await WriteAsync(db, SharedDirtyKey, "0");
        await db.SaveChangesAsync();
        Current = s;
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }
}

public sealed class ValidationException : Exception
{
    public ValidationException(IEnumerable<string> errors) : base(string.Join(Environment.NewLine, errors))
    {
        Errors = errors.ToList();
    }

    public IReadOnlyList<string> Errors { get; }
}
