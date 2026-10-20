using System.Collections.ObjectModel;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EEMC.WM.App.Infrastructure;
using EEMC.WM.Core.Domain;
using EEMC.WM.Core.Text;
using EEMC.WM.Core.Time;
using EEMC.WM.Data.Infrastructure;
using EEMC.WM.Data.Services;

namespace EEMC.WM.App.ViewModels;

public sealed class NotificationItemViewModel
{
    public NotificationItemViewModel(AppNotification n, MainViewModel owner)
    {
        N = n;
        OpenCommand = new AsyncRelayCommand(() => owner.OpenNotificationAsync(this));
        MarkReadCommand = new AsyncRelayCommand(() => owner.MarkReadAsync(this));
        SnoozeCommand = new AsyncRelayCommand<string>(m => owner.SnoozeAsync(this, m ?? "15"));
    }

    public AppNotification N { get; }
    public string Title => N.Title;
    public string Message => N.Message;
    public string TimeText => Labels.DateTime(N.CreatedAt);
    public bool IsRead => N.IsRead;
    public bool HasTask => N.TaskId.HasValue;
    public string Color => N.Severity switch
    {
        NotificationSeverity.Critical => "Red",
        NotificationSeverity.Warning => "Orange",
        _ => "Blue"
    };
    public IAsyncRelayCommand OpenCommand { get; }
    public IAsyncRelayCommand MarkReadCommand { get; }
    public IAsyncRelayCommand<string> SnoozeCommand { get; }
}

public sealed partial class MainViewModel : ObservableObject
{
    private readonly Dictionary<PageKey, PageViewModel> _pages;
    private readonly TaskService _tasks;
    private readonly NotificationService _notifications;
    private readonly TaskActions _actions;
    private readonly IClock _clock;
    private readonly DispatcherTimer _clockTimer;
    private readonly DispatcherTimer _refreshDebounce;
    private DateTime _lastMinuteRefresh;
    private readonly SyncHost _sync;

    // ---- Trạng thái đồng bộ (thanh trên)
    [ObservableProperty] private string _syncText = "";
    [ObservableProperty] private string _syncColor = "Gray";
    [ObservableProperty] private string _syncGlyph = "";
    [ObservableProperty] private bool _syncVisible;

    private void UpdateSyncStatus()
    {
        var r = _sync.Service.LastResult;
        SyncVisible = r.State != SyncState.Disabled;
        (SyncText, SyncColor, SyncGlyph) = r.State switch
        {
            SyncState.Running => ("Đang đồng bộ...", "Blue", ""),
            SyncState.Ok => ($"Đã đồng bộ {r.At:HH:mm}", "Green", ""),
            SyncState.Error => ("Lỗi đồng bộ", "Red", ""),
            _ => ("", "Gray", "")
        };
        OnPropertyChanged(nameof(SyncTooltip));
    }

    public string SyncTooltip => _sync.Service.LastResult.Message + "\nBấm để đồng bộ ngay.";

    [RelayCommand]
    private async Task SyncNow()
    {
        await _sync.RunAsync();
        OnPropertyChanged(nameof(SyncTooltip));
    }

    public MainViewModel(
        DashboardViewModel dashboard, TodayViewModel today, TaskListViewModel list, CalendarViewModel calendar,
        DocumentsViewModel documents, SettingsViewModel settings,
        TaskService tasks, NotificationService notifications, CategoryService categories, TaskActions actions,
        Navigator nav, IClock clock, SyncHost sync)
    {
        _sync = sync;
        sync.Service.StatusChanged += (_, _) =>
            System.Windows.Application.Current?.Dispatcher.BeginInvoke(UpdateSyncStatus);
        _tasks = tasks;
        _notifications = notifications;
        _actions = actions;
        _clock = clock;

        _pages = new Dictionary<PageKey, PageViewModel>
        {
            [PageKey.Dashboard] = dashboard,
            [PageKey.Today] = today,
            [PageKey.Tasks] = list,
            [PageKey.Calendar] = calendar,
            [PageKey.Projects] = new PlannedFeatureViewModel("Dự án KH&CN",
                "Quản lý đề tài/dự án KH&CN thuộc Giai đoạn 2. Hiện tại có thể ghi \"Tên đề tài/dự án liên quan\" trong Thông tin mở rộng của công việc và tìm kiếm theo tên dự án.",
                new[]
                {
                    "Mã, tên, cấp quản lý, đơn vị chủ trì, chủ nhiệm, thời gian thực hiện",
                    "Các mốc quan trọng, danh sách công việc, hồ sơ liên quan",
                    "Tiến độ tổng thể tính từ công việc con (có trọng số tùy chọn)",
                    "Vấn đề tồn đọng, các quyết định cần xin ý kiến",
                    "Mẫu dự án nghiệp vụ (không tự tạo tiến độ/ngày hoàn thành)"
                }),
            [PageKey.Documents] = documents,
            [PageKey.Reports] = new PlannedFeatureViewModel("Báo cáo",
                "Báo cáo tự động thuộc Giai đoạn 2. Hiện tại có thể lọc danh sách công việc (theo tuần/tháng, trạng thái) và Xuất Excel; Tổng quan có số liệu tuần và tỷ lệ đúng hạn.",
                new[]
                {
                    "Báo cáo ngày: đã hoàn thành, đang xử lý, quá hạn, việc ngày tiếp theo",
                    "Báo cáo tuần: tiếp nhận, hoàn thành, tồn, chờ phối hợp, chờ phê duyệt, kế hoạch tuần sau",
                    "Báo cáo tháng: kết quả, phân tích tiến độ, tồn đọng, tỷ lệ hoàn thành, trọng tâm tháng sau",
                    "Xuất Word, Excel, PDF theo văn phong hành chính EEMC",
                    "Chỉ tổng hợp dữ liệu thực tế, không suy diễn kết quả"
                }),
            [PageKey.Settings] = settings,
        };

        tasks.TasksChanged += (_, _) => ScheduleRefresh();
        notifications.Changed += (_, _) => _ = LoadNotificationsAsync();
        settings.CategoriesChanged += (_, _) => list.InvalidateCategories();
        ThemeManager.ThemeChanged += (_, _) => ScheduleRefresh();
        nav.NavigateRequested += k => _ = NavigateAsync(k);
        nav.TaskListRequested += r =>
        {
            list.Apply(r);
            _ = NavigateAsync(PageKey.Tasks, force: true);
        };

        _refreshDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        _refreshDebounce.Tick += async (_, _) =>
        {
            _refreshDebounce.Stop();
            await RefreshCurrentAsync();
        };

        _clockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
        _clockTimer.Tick += (_, _) => TickClock();
        TickClock();
        _lastMinuteRefresh = _clock.Now;
        _clockTimer.Start();

        _currentPage = dashboard;
    }

    [ObservableProperty] private PageViewModel _currentPage;
    [ObservableProperty] private PageKey _selectedPage = PageKey.Dashboard;
    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private string _clockText = "";
    [ObservableProperty] private string _dateText = "";
    [ObservableProperty] private int _unreadCount;
    [ObservableProperty] private bool _isNotificationsOpen;

    public ObservableCollection<NotificationItemViewModel> Notifications { get; } = new();

    partial void OnSelectedPageChanged(PageKey value) => _ = NavigateAsync(value);

    private void TickClock()
    {
        var now = _clock.Now;
        ClockText = now.ToString("HH:mm");
        DateText = $"{Labels.DayOfWeekVi(now.DayOfWeek)}, {now:dd/MM/yyyy}";
        // Thời gian còn lại thay đổi theo thời gian: làm mới màn hình 5 phút/lần
        if ((now - _lastMinuteRefresh).TotalMinutes >= 5)
        {
            _lastMinuteRefresh = now;
            if (CurrentPage is DashboardViewModel or TodayViewModel) ScheduleRefresh();
        }
    }

    public void ScheduleRefresh()
    {
        _refreshDebounce.Stop();
        _refreshDebounce.Start();
    }

    public async Task InitializeAsync()
    {
        await LoadNotificationsAsync();
        await NavigateAsync(PageKey.Dashboard, force: true);
    }

    public async Task NavigateAsync(PageKey key, bool force = false)
    {
        if (SelectedPage != key)
        {
            SetProperty(ref _selectedPage, key, nameof(SelectedPage));
        }
        var page = _pages[key];
        if (CurrentPage == page && !force) return;
        CurrentPage = page;
        await SafeRefresh(page);
    }

    private async Task RefreshCurrentAsync() => await SafeRefresh(CurrentPage);

    private static async Task SafeRefresh(PageViewModel page)
    {
        try { await page.RefreshAsync(); }
        catch (Exception ex) { AppLog.Error(ex, $"Làm mới màn hình {page.Title}"); }
    }

    [RelayCommand]
    private Task AddTask() => _actions.AddNewAsync();

    [RelayCommand]
    private void Search()
    {
        var text = SearchText?.Trim();
        if (string.IsNullOrEmpty(text)) return;
        var list = (TaskListViewModel)_pages[PageKey.Tasks];
        list.Apply(new TaskListRequest { Search = text, Status = StatusFilter.All });
        _ = NavigateAsync(PageKey.Tasks, force: true);
    }

    [RelayCommand]
    private Task Go(PageKey key) => NavigateAsync(key);

    [RelayCommand]
    private async Task ToggleNotifications()
    {
        if (!IsNotificationsOpen) await LoadNotificationsAsync();
        IsNotificationsOpen = !IsNotificationsOpen;
    }

    public async Task LoadNotificationsAsync()
    {
        try
        {
            var list = await _notifications.GetRecentAsync();
            Notifications.Clear();
            foreach (var n in list) Notifications.Add(new NotificationItemViewModel(n, this));
            UnreadCount = await _notifications.CountUnreadAsync();
        }
        catch (Exception ex) { AppLog.Error(ex, "Tải thông báo"); }
    }

    [RelayCommand]
    private async Task MarkAllRead()
    {
        await _notifications.MarkAllReadAsync();
    }

    public async Task OpenNotificationAsync(NotificationItemViewModel n)
    {
        await _notifications.MarkReadAsync(n.N.Id);
        IsNotificationsOpen = false;
        if (n.N.TaskId is int id) await _actions.EditAsync(id);
    }

    public async Task MarkReadAsync(NotificationItemViewModel n) => await _notifications.MarkReadAsync(n.N.Id);

    public async Task SnoozeAsync(NotificationItemViewModel n, string mode)
    {
        if (n.N.TaskId is int id) await _actions.SnoozeAsync(id, mode);
        await _notifications.MarkReadAsync(n.N.Id);
    }

    public void OpenNotificationCenter()
    {
        _ = LoadNotificationsAsync();
        IsNotificationsOpen = true;
    }
}
