using System.Windows;
using System.Windows.Threading;
using EEMC.WM.Data.Infrastructure;
using EEMC.WM.Data.Services;
using Forms = System.Windows.Forms;

namespace EEMC.WM.App.Infrastructure;

/// <summary>Biểu tượng khay hệ thống + thông báo Windows (balloon hiển thị dạng toast trên Windows 10/11).</summary>
public sealed class TrayService : IDisposable
{
    private Forms.NotifyIcon? _icon;
    public event Action? OpenRequested;
    public event Action? AddTaskRequested;
    public event Action? ExitRequested;
    public event Action? BalloonClicked;

    public void Initialize()
    {
        System.Drawing.Icon? ico = null;
        try { if (Environment.ProcessPath != null) ico = System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath); }
        catch { /* dùng biểu tượng mặc định */ }

        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Mở EEMC KHCN Work Manager", null, (_, _) => OpenRequested?.Invoke());
        menu.Items.Add("Thêm công việc mới", null, (_, _) => AddTaskRequested?.Invoke());
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Thoát", null, (_, _) => ExitRequested?.Invoke());

        _icon = new Forms.NotifyIcon
        {
            Icon = ico ?? System.Drawing.SystemIcons.Application,
            Text = "EEMC KHCN Work Manager",
            Visible = true,
            ContextMenuStrip = menu
        };
        _icon.DoubleClick += (_, _) => OpenRequested?.Invoke();
        _icon.BalloonTipClicked += (_, _) => BalloonClicked?.Invoke();
    }

    public void Notify(string title, string message, bool critical)
    {
        if (_icon == null) return;
        try
        {
            var text = message.Length > 250 ? message[..247] + "..." : message;
            _icon.ShowBalloonTip(10000, title, text, critical ? Forms.ToolTipIcon.Warning : Forms.ToolTipIcon.Info);
        }
        catch (Exception ex) { AppLog.Error(ex, "Hiển thị thông báo Windows"); }
    }

    public void Dispose()
    {
        if (_icon != null)
        {
            _icon.Visible = false;
            _icon.Dispose();
            _icon = null;
        }
    }
}

/// <summary>Chạy kiểm tra nhắc việc mỗi phút và ngay khi khởi động.</summary>
public sealed class ReminderHost
{
    private readonly ReminderService _reminders;
    private readonly NotificationService _notifications;
    private readonly SettingsService _settings;
    private readonly TrayService _tray;
    private readonly DispatcherTimer _timer;

    public ReminderHost(ReminderService reminders, NotificationService notifications, SettingsService settings, TrayService tray)
    {
        _reminders = reminders;
        _notifications = notifications;
        _settings = settings;
        _tray = tray;
        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(60) };
        _timer.Tick += async (_, _) => await CheckAsync();
    }

    public async Task StartAsync()
    {
        await CheckAsync();
        _timer.Start();
    }

    public void Stop() => _timer.Stop();

    public async Task CheckAsync()
    {
        try
        {
            var created = await _reminders.CheckAsync();
            if (created.Count == 0) return;
            _notifications.RaiseChanged();
            if (!_settings.Current.WindowsNotification) return;

            if (created.Count <= 2)
            {
                foreach (var n in created)
                    _tray.Notify(n.Title, n.Message, n.Severity == Core.Domain.NotificationSeverity.Critical);
            }
            else
            {
                int critical = created.Count(n => n.Severity == Core.Domain.NotificationSeverity.Critical);
                _tray.Notify($"{created.Count} nhắc việc mới",
                    (critical > 0 ? $"{critical} công việc quá hạn/đến hạn. " : "") + "Mở trung tâm thông báo để xem chi tiết.",
                    critical > 0);
            }
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "Kiểm tra nhắc việc");
        }
    }
}
