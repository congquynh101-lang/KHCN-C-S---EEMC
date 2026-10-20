using System.Windows.Threading;
using EEMC.WM.Data.Infrastructure;
using EEMC.WM.Data.Services;

namespace EEMC.WM.App.Infrastructure;

/// <summary>
/// Lên lịch đồng bộ: khi mở phần mềm, định kỳ (mặc định 2 phút), và ~10 giây sau mỗi thay đổi trên máy này.
/// </summary>
public sealed class SyncHost
{
    private readonly SyncService _sync;
    private readonly SettingsService _settings;
    private readonly TaskService _tasks;
    private readonly CategoryService _categories;
    private readonly ReminderHost _reminders;
    private readonly DispatcherTimer _periodic;
    private readonly DispatcherTimer _afterChange;
    private bool _notifyingImport;

    public SyncHost(SyncService sync, SettingsService settings, TaskService tasks, CategoryService categories, ReminderHost reminders)
    {
        _sync = sync;
        _settings = settings;
        _tasks = tasks;
        _categories = categories;
        _reminders = reminders;

        _periodic = new DispatcherTimer(DispatcherPriority.Background);
        _periodic.Tick += async (_, _) => await RunAsync();
        _afterChange = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
        _afterChange.Tick += async (_, _) => { _afterChange.Stop(); await RunAsync(); };

        _tasks.TasksChanged += (_, _) => { if (!_notifyingImport) Kick(); };
        _categories.Changed += (_, _) => Kick();
        _settings.Changed += (_, _) => ApplyInterval();
        _sync.DataImported += (_, _) => System.Windows.Application.Current.Dispatcher.BeginInvoke(async () =>
        {
            _notifyingImport = true;
            try { _tasks.NotifyExternalChange(); }
            finally { _notifyingImport = false; }
            await _reminders.CheckAsync();
        });
    }

    public SyncService Service => _sync;

    public async Task StartAsync()
    {
        ApplyInterval();
        await RunAsync();
    }

    private void ApplyInterval()
    {
        var minutes = Math.Clamp(_settings.Current.SyncIntervalMinutes, 1, 60);
        _periodic.Interval = TimeSpan.FromMinutes(minutes);
        if (_settings.Current.SyncEnabled) _periodic.Start();
        else _periodic.Stop();
    }

    /// <summary>Có thay đổi trên máy này: hẹn đồng bộ sau vài giây (gộp nhiều thao tác liên tiếp).</summary>
    public void Kick()
    {
        if (!_settings.Current.SyncEnabled) return;
        _afterChange.Stop();
        _afterChange.Start();
    }

    public async Task<SyncResult> RunAsync()
    {
        try { return await _sync.RunAsync(); }
        catch (Exception ex)
        {
            AppLog.Error(ex, "Đồng bộ");
            return _sync.LastResult;
        }
    }

    public void Stop()
    {
        _periodic.Stop();
        _afterChange.Stop();
    }
}
