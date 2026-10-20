using System.Globalization;
using System.Windows;
using System.Windows.Markup;
using System.Windows.Threading;
using EEMC.WM.App.Infrastructure;
using EEMC.WM.App.ViewModels;
using EEMC.WM.App.Views;
using EEMC.WM.Core.Time;
using EEMC.WM.Data;
using EEMC.WM.Data.Infrastructure;
using EEMC.WM.Data.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EEMC.WM.App;

public partial class App : Application
{
    private const string MutexName = "EEMC.KHCN.WorkManager.SingleInstance";
    private const string ShowEventName = "EEMC.KHCN.WorkManager.Show";
    private Mutex? _mutex;
    private EventWaitHandle? _showEvent;
    private ServiceProvider? _services;
    private static bool _restarting;

    public static IServiceProvider Services => ((App)Current)._services!;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var paths = new AppPaths();

        // Chỉ cho chạy một phiên bản trên mỗi thư mục dữ liệu; mở lại sẽ đưa cửa sổ hiện có lên trước
        var instanceKey = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(paths.DataDir.TrimEnd('\\').ToLowerInvariant())))[..16];
        _mutex = new Mutex(true, $"{MutexName}.{instanceKey}", out bool isFirst);
        if (!isFirst)
        {
            try { EventWaitHandle.OpenExisting($"{ShowEventName}.{instanceKey}").Set(); } catch { }
            Shutdown();
            return;
        }
        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, $"{ShowEventName}.{instanceKey}");

        var culture = CultureInfo.GetCultureInfo("vi-VN");
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        Thread.CurrentThread.CurrentCulture = culture;
        Thread.CurrentThread.CurrentUICulture = culture;
        FrameworkElement.LanguageProperty.OverrideMetadata(typeof(FrameworkElement),
            new FrameworkPropertyMetadata(XmlLanguage.GetLanguage(culture.IetfLanguageTag)));

        AppLog.Initialize(paths.LogsDir);
        AppLog.Info("Khởi động EEMC KHCN Work Manager");
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, ex) => AppLog.Error((Exception)ex.ExceptionObject, "Lỗi không xử lý (AppDomain)");
        TaskScheduler.UnobservedTaskException += (_, ex) => { AppLog.Error(ex.Exception, "Lỗi tác vụ nền"); ex.SetObserved(); };

        try
        {
            _services = ConfigureServices(paths);
            AppDbContext.CurrentDeviceId = _services.GetRequiredService<DeviceIdentity>().DeviceId;
            await DbInitializer.InitializeAsync(_services.GetRequiredService<IDbContextFactory<AppDbContext>>());
            var settings = _services.GetRequiredService<SettingsService>();
            await settings.LoadAsync();
            ThemeManager.Apply(settings.Current.DarkTheme);
            WindowTheming.Register();

            await _services.GetRequiredService<BackupService>().AutoBackupIfNeededAsync();
            await _services.GetRequiredService<NotificationService>().CleanupAsync();

            var main = _services.GetRequiredService<MainWindow>();
            MainWindow = main;
            main.Show();
            await ((MainViewModel)main.DataContext).InitializeAsync();

            var tray = _services.GetRequiredService<TrayService>();
            tray.Initialize();
            tray.OpenRequested += () => main.ShowFromTray();
            tray.BalloonClicked += () =>
            {
                main.ShowFromTray();
                ((MainViewModel)main.DataContext).OpenNotificationCenter();
            };
            tray.AddTaskRequested += async () =>
            {
                main.ShowFromTray();
                await _services.GetRequiredService<TaskActions>().AddNewAsync();
            };
            tray.ExitRequested += () => main.ExitApplication();

            // Lắng nghe yêu cầu mở từ phiên bản thứ hai
            var showEvent = _showEvent;
            var thread = new Thread(() =>
            {
                while (showEvent.WaitOne())
                    Dispatcher.BeginInvoke(() => main.ShowFromTray());
            }) { IsBackground = true, Name = "SingleInstanceListener" };
            thread.Start();

            // Kiểm tra nhắc việc ngay khi mở (kể cả các mốc đã đến khi ứng dụng không chạy)
            await _services.GetRequiredService<ReminderHost>().StartAsync();

            // Đồng bộ với các máy khác (nếu đã bật)
            await _services.GetRequiredService<SyncHost>().StartAsync();
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "Không khởi động được ứng dụng");
            MessageBox.Show("Không khởi động được phần mềm.\n\n" + ex.Message + $"\n\nNhật ký lỗi: {paths.LogsDir}",
                "EEMC KHCN Work Manager", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    private static ServiceProvider ConfigureServices(AppPaths paths)
    {
        var s = new ServiceCollection();
        s.AddSingleton(paths);
        s.AddSingleton<IClock, VietnamClock>();
        s.AddDbContextFactory<AppDbContext>(o => o.UseSqlite(paths.ConnectionString));

        s.AddSingleton<SettingsService>();
        s.AddSingleton<TaskService>();
        s.AddSingleton<CategoryService>();
        s.AddSingleton<NotificationService>();
        s.AddSingleton<ReminderService>();
        s.AddSingleton<DashboardService>();
        s.AddSingleton<BackupService>();
        s.AddSingleton<ExcelExportService>();
        s.AddSingleton<DeviceIdentity>();
        s.AddSingleton<SyncService>();
        s.AddSingleton<SyncHost>();

        s.AddSingleton<DialogService>();
        s.AddSingleton<Navigator>();
        s.AddSingleton<TaskActions>();
        s.AddSingleton<TrayService>();
        s.AddSingleton<ReminderHost>();

        s.AddSingleton<DashboardViewModel>();
        s.AddSingleton<TodayViewModel>();
        s.AddSingleton<TaskListViewModel>();
        s.AddSingleton<CalendarViewModel>();
        s.AddSingleton<DocumentsViewModel>();
        s.AddSingleton<SettingsViewModel>();
        s.AddSingleton<MainViewModel>();
        s.AddTransient<TaskEditorViewModel>();
        s.AddSingleton<MainWindow>();
        return s.BuildServiceProvider();
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        AppLog.Error(e.Exception, "Lỗi giao diện không xử lý");
        MessageBox.Show("Đã xảy ra lỗi không mong muốn. Dữ liệu đã lưu không bị ảnh hưởng.\n\n" + e.Exception.Message,
            "EEMC KHCN Work Manager", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }

    /// <summary>Khởi động lại ứng dụng (sau khi phục hồi dữ liệu).</summary>
    public static void Restart()
    {
        _restarting = true;
        var app = (App)Current;
        app.ReleaseSingleInstance();
        if (Environment.ProcessPath != null)
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Environment.ProcessPath) { UseShellExecute = true });
        if (app.MainWindow is MainWindow mw) mw.ExitApplication();
        else app.Shutdown();
    }

    private void ReleaseSingleInstance()
    {
        try { _mutex?.ReleaseMutex(); } catch { }
        _mutex?.Dispose();
        _mutex = null;
        _showEvent?.Dispose();
        _showEvent = null;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            _services?.GetService<ReminderHost>()?.Stop();
            _services?.GetService<SyncHost>()?.Stop();
            // Gửi nốt các thay đổi chưa đồng bộ trước khi thoát (tối đa 8 giây)
            var sync = _services?.GetService<SyncService>();
            if (sync != null && _services!.GetRequiredService<SettingsService>().Current.SyncEnabled)
                Task.Run(() => sync.RunAsync()).Wait(TimeSpan.FromSeconds(8));
            _services?.GetService<TrayService>()?.Dispose();
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            _services?.Dispose();
        }
        catch (Exception ex) { AppLog.Error(ex, "Đóng ứng dụng"); }
        if (!_restarting) ReleaseSingleInstance();
        AppLog.Info("Đóng ứng dụng");
        base.OnExit(e);
    }
}
