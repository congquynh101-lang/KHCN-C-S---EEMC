using EEMC.WM.Core.Domain;
using EEMC.WM.Core.Services;
using EEMC.WM.Core.Time;
using Microsoft.EntityFrameworkCore;

namespace EEMC.WM.Data.Services;

/// <summary>
/// Kiểm tra nhắc việc định kỳ (và ngay khi mở ứng dụng): tạo thông báo mới trong CSDL,
/// ghi nhận mốc đã nhắc để không gửi trùng.
/// </summary>
public sealed class ReminderService
{
    private readonly IDbContextFactory<AppDbContext> _factory;
    private readonly IClock _clock;
    private readonly SettingsService _settings;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public ReminderService(IDbContextFactory<AppDbContext> factory, IClock clock, SettingsService settings)
    {
        _factory = factory;
        _clock = clock;
        _settings = settings;
    }

    /// <returns>Các thông báo mới được tạo trong lần kiểm tra này.</returns>
    public async Task<List<AppNotification>> CheckAsync()
    {
        if (!await _gate.WaitAsync(0)) return new List<AppNotification>();
        try
        {
            var now = _clock.Now;
            var settings = _settings.Current;
            await using var db = await _factory.CreateDbContextAsync();

            var tasks = await db.Tasks
                .Where(t => !t.IsDeleted && t.Status != WorkTaskStatus.Completed && t.Status != WorkTaskStatus.Cancelled)
                .ToListAsync();
            if (tasks.Count == 0) return new List<AppNotification>();

            var ids = tasks.Select(t => t.Id).ToList();
            var logs = (await db.ReminderLogs.AsNoTracking().Where(r => ids.Contains(r.TaskId))
                    .Select(r => new { r.TaskId, r.ReminderKey }).ToListAsync())
                .GroupBy(r => r.TaskId)
                .ToDictionary(g => g.Key, g => (ISet<string>)g.Select(x => x.ReminderKey).ToHashSet());

            var created = new List<AppNotification>();
            foreach (var t in tasks)
            {
                var fired = logs.TryGetValue(t.Id, out var set) ? set : new HashSet<string>();
                var decision = ReminderPlanner.Decide(t, settings, now, fired);
                if (decision.IsEmpty) continue;

                foreach (var key in decision.KeysToLog.Distinct())
                    db.ReminderLogs.Add(new ReminderLog { TaskId = t.Id, ReminderKey = key, FiredAt = now });

                if (decision.Message != null)
                {
                    var n = new AppNotification
                    {
                        TaskId = t.Id,
                        CreatedAt = now,
                        Title = decision.Message.Title,
                        Message = decision.Message.Message,
                        Severity = decision.Message.Severity
                    };
                    db.Notifications.Add(n);
                    created.Add(n);
                }

                // Hết tạm hoãn: xóa mốc tạm hoãn để lần tạm hoãn sau hoạt động bình thường
                if (t.SnoozeUntil.HasValue && t.SnoozeUntil.Value <= now) t.SnoozeUntil = null;
            }
            await db.SaveChangesAsync();
            return created;
        }
        finally
        {
            _gate.Release();
        }
    }
}
