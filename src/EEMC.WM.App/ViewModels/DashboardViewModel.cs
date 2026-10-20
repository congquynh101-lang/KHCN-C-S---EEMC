using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EEMC.WM.App.Infrastructure;
using EEMC.WM.Core.Domain;
using EEMC.WM.Core.Services;
using EEMC.WM.Core.Time;
using EEMC.WM.Data.Services;

namespace EEMC.WM.App.ViewModels;

public sealed class KpiTile
{
    public required string Label { get; init; }
    public int Value { get; init; }
    public string Color { get; init; } = "Blue";
    public string Hint { get; init; } = "";
    public string Glyph { get; init; } = "\uE8FD";
    public required IRelayCommand OpenCommand { get; init; }
}

public sealed class BarItem
{
    public required string Label { get; init; }
    public double Value { get; init; }
    public double Max { get; init; }
    public string Color { get; init; } = "Blue";
    public string ValueText => Value.ToString("0");
}

public sealed class WeekBar
{
    public required string Label { get; init; }
    public double Received { get; init; }
    public double Completed { get; init; }
    public double Backlog { get; init; }
    public double Max { get; init; }
    public double BacklogMax { get; init; }
    public string Tooltip => $"Tuần từ {Label}: nhận {Received:0}, hoàn thành {Completed:0}, tồn đọng cuối tuần {Backlog:0}";
}

public sealed partial class DashboardViewModel : PageViewModel
{
    private readonly DashboardService _dashboard;
    private readonly TaskService _tasks;
    private readonly SettingsService _settings;
    private readonly TaskActions _actions;
    private readonly Navigator _nav;
    private readonly IClock _clock;

    public DashboardViewModel(DashboardService dashboard, TaskService tasks, SettingsService settings, TaskActions actions, Navigator nav, IClock clock)
    {
        _dashboard = dashboard;
        _tasks = tasks;
        _settings = settings;
        _actions = actions;
        _nav = nav;
        _clock = clock;
    }

    public override string Title => "Tổng quan";

    public ObservableCollection<KpiTile> Tiles { get; } = new();
    public ObservableCollection<TaskItemViewModel> TopFive { get; } = new();
    public ObservableCollection<BarItem> ByStatus { get; } = new();
    public ObservableCollection<BarItem> ByCategory { get; } = new();
    public ObservableCollection<WeekBar> Weekly { get; } = new();

    [ObservableProperty] private string _onTimeText = "—";
    [ObservableProperty] private string _onTimeDetail = "";
    [ObservableProperty] private string _leadTimeText = "—";
    [ObservableProperty] private string _greeting = "";
    [ObservableProperty] private bool _hasNoTasks;

    public override async Task RefreshAsync()
    {
        IsLoading = true;
        try
        {
            var now = _clock.Now;
            var stats = await _dashboard.GetStatsAsync();
            var open = await _tasks.GetOpenTasksAsync();
            var ranked = new PriorityEngine(_settings.Current).RankDirectWork(open, now);

            Greeting = (now.Hour < 11 ? "Chào buổi sáng" : now.Hour < 13 ? "Chào buổi trưa" : now.Hour < 18 ? "Chào buổi chiều" : "Chào buổi tối") + $" — {Core.Text.Labels.DayOfWeekVi(now.DayOfWeek)}, {now:dd/MM/yyyy}";
            HasNoTasks = stats.Open == 0;

            Tiles.Clear();
            void Tile(string label, int value, string color, string hint, TaskListRequest req, string glyph) =>
                Tiles.Add(new KpiTile { Label = label, Value = value, Color = color, Hint = hint, Glyph = glyph, OpenCommand = new RelayCommand(() => _nav.ShowTasks(req)) });

            Tile("Chưa hoàn thành", stats.Open, "Blue", "Tổng số công việc đang mở", new TaskListRequest(), "\uE8FD");
            Tile("Cần xử lý hôm nay", stats.Today, "Orange", "Hạn hoặc lịch thực hiện hôm nay", new TaskListRequest { Time = TimeFilter.Today }, "\uE823");
            Tile("Ưu tiên cao / khẩn cấp", stats.HighOrUrgent, "Orange", "Mức ưu tiên Cao hoặc Khẩn cấp", new TaskListRequest { HighOrUrgent = true }, "\uE7C1");
            Tile("Quá hạn", stats.Overdue, "Red", "Đã quá hạn, chưa hoàn thành", new TaskListRequest { Time = TimeFilter.Overdue }, "\uE7BA");
            Tile("Sắp đến hạn", stats.DueSoon, "Orange", $"Đến hạn trong {_settings.Current.DueSoonDays} ngày tới", new TaskListRequest { Time = TimeFilter.DueSoon }, "\uE916");
            Tile("Chờ phản hồi / phối hợp", stats.WaitingResponse, "Gray", "Chờ đơn vị khác phản hồi", new TaskListRequest { Status = StatusFilter.Waiting }, "\uE716");
            Tile("Chờ lãnh đạo phê duyệt", stats.WaitingApproval, "Gray", "Đã trình, chờ phê duyệt", new TaskListRequest { Status = StatusFilter.Specific, SpecificStatus = WorkTaskStatus.WaitingApproval }, "\uE8F4");
            Tile("Hoàn thành trong tuần", stats.CompletedThisWeek, "Green", "Từ thứ Hai tuần này", new TaskListRequest { Status = StatusFilter.All, Time = TimeFilter.CompletedThisWeek }, "\uE930");

            TopFive.Clear();
            foreach (var a in ranked.Take(5)) TopFive.Add(new TaskItemViewModel(a.Task, now, _actions, a));

            ByStatus.Clear();
            var maxS = Math.Max(1, stats.ByStatus.Max(p => p.Value));
            foreach (var p in stats.ByStatus)
                ByStatus.Add(new BarItem { Label = p.Label, Value = p.Value, Max = maxS, Color = StatusColor(p.Key) });

            ByCategory.Clear();
            var maxC = Math.Max(1, stats.ByCategory.Select(p => p.Value).DefaultIfEmpty(0).Max());
            foreach (var p in stats.ByCategory)
                ByCategory.Add(new BarItem { Label = p.Label, Value = p.Value, Max = maxC, Color = "Blue" });

            Weekly.Clear();
            var maxW = Math.Max(1, stats.Weekly.Select(w => Math.Max(w.Received, w.Completed)).DefaultIfEmpty(0).Max());
            var maxB = Math.Max(1, stats.Weekly.Select(w => w.Backlog).DefaultIfEmpty(0).Max());
            foreach (var w in stats.Weekly)
                Weekly.Add(new WeekBar { Label = w.Label, Received = w.Received, Completed = w.Completed, Backlog = w.Backlog, Max = maxW, BacklogMax = maxB });

            OnTimeText = stats.OnTimeRate.HasValue ? $"{stats.OnTimeRate.Value:P0}" : "—";
            OnTimeDetail = stats.CompletedLast8Weeks == 0
                ? "Chưa có công việc hoàn thành trong 8 tuần gần nhất"
                : $"{stats.CompletedOnTimeLast8Weeks}/{stats.CompletedLast8Weeks} công việc hoàn thành đúng hạn (8 tuần)";
            LeadTimeText = stats.AvgLeadDays.HasValue ? $"{stats.AvgLeadDays.Value:0.#} ngày" : "—";
        }
        finally { IsLoading = false; }
    }

    private static string StatusColor(string? key) => key switch
    {
        nameof(WorkTaskStatus.InProgress) => "Blue",
        nameof(WorkTaskStatus.NeedMoreDocuments) => "Orange",
        nameof(WorkTaskStatus.NotStarted) => "Gray",
        nameof(WorkTaskStatus.OnHold) => "Gray",
        _ => "Gray"
    };

    [RelayCommand]
    private Task AddTask() => _actions.AddNewAsync();

    [RelayCommand]
    private void OpenToday() => _nav.Go(PageKey.Today);
}
