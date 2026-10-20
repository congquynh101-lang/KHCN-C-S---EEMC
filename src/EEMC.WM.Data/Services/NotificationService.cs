using EEMC.WM.Core.Domain;
using EEMC.WM.Core.Time;
using Microsoft.EntityFrameworkCore;

namespace EEMC.WM.Data.Services;

/// <summary>Trung tâm thông báo trong ứng dụng.</summary>
public sealed class NotificationService
{
    private readonly IDbContextFactory<AppDbContext> _factory;
    private readonly IClock _clock;

    public NotificationService(IDbContextFactory<AppDbContext> factory, IClock clock)
    {
        _factory = factory;
        _clock = clock;
    }

    public event EventHandler? Changed;
    public void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);

    public async Task<List<AppNotification>> GetRecentAsync(int take = 200)
    {
        await using var db = await _factory.CreateDbContextAsync();
        return await db.Notifications.AsNoTracking().OrderBy(n => n.IsRead).ThenByDescending(n => n.CreatedAt).ThenByDescending(n => n.Id)
            .Take(take).ToListAsync();
    }

    public async Task<int> CountUnreadAsync()
    {
        await using var db = await _factory.CreateDbContextAsync();
        return await db.Notifications.CountAsync(n => !n.IsRead);
    }

    public async Task MarkReadAsync(int id)
    {
        await using var db = await _factory.CreateDbContextAsync();
        await db.Notifications.Where(n => n.Id == id).ExecuteUpdateAsync(s => s.SetProperty(n => n.IsRead, true));
        RaiseChanged();
    }

    public async Task MarkAllReadAsync()
    {
        await using var db = await _factory.CreateDbContextAsync();
        await db.Notifications.Where(n => !n.IsRead).ExecuteUpdateAsync(s => s.SetProperty(n => n.IsRead, true));
        RaiseChanged();
    }

    public async Task MarkTaskReadAsync(int taskId)
    {
        await using var db = await _factory.CreateDbContextAsync();
        await db.Notifications.Where(n => n.TaskId == taskId && !n.IsRead).ExecuteUpdateAsync(s => s.SetProperty(n => n.IsRead, true));
        RaiseChanged();
    }

    /// <summary>Dọn thông báo đã đọc cũ hơn 90 ngày.</summary>
    public async Task CleanupAsync()
    {
        await using var db = await _factory.CreateDbContextAsync();
        var limit = _clock.Now.AddDays(-90);
        await db.Notifications.Where(n => n.IsRead && n.CreatedAt < limit).ExecuteDeleteAsync();
    }
}
