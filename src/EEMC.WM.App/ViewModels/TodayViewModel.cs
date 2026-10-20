using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EEMC.WM.App.Infrastructure;
using EEMC.WM.Core.Domain;
using EEMC.WM.Core.Services;
using EEMC.WM.Core.Text;
using EEMC.WM.Core.Time;
using EEMC.WM.Data.Services;

namespace EEMC.WM.App.ViewModels;

public sealed class ScheduleBlockItem
{
    public required ScheduledBlock Block { get; init; }
    public required TaskItemViewModel Item { get; init; }
    public string TimeText => $"{Block.Start:HH:mm} – {Block.End:HH:mm}";
    public string DurationText => $"{Block.Hours:0.##} giờ" + (Block.IsEstimate ? " (ước tính)" : "");
    public bool IsFixed => Block.IsFixed;
    public bool IsEstimate => Block.IsEstimate;
    public bool IsApplied => Item.Task.PlannedStart.HasValue && Item.Task.PlannedStart.Value == Block.Start;
    public string KindText => Block.IsFixed ? "Lịch cố định" : IsApplied ? "Đã đưa vào kế hoạch" : "Đề xuất";
    public required IAsyncRelayCommand ApplyCommand { get; init; }
}

public sealed class WarningItem
{
    public required string Message { get; init; }
    public required TaskItemViewModel Item { get; init; }
    public bool IsCertain { get; init; }
    public string Color => IsCertain ? "Red" : "Orange";
}

/// <summary>Công việc hôm nay: thứ tự xử lý đề xuất, lịch đề xuất trong ngày, công việc chờ cần đôn đốc.</summary>
public sealed partial class TodayViewModel : PageViewModel
{
    private readonly TaskService _tasks;
    private readonly SettingsService _settings;
    private readonly TaskActions _actions;
    private readonly IClock _clock;

    public TodayViewModel(TaskService tasks, SettingsService settings, TaskActions actions, IClock clock)
    {
        _tasks = tasks;
        _settings = settings;
        _actions = actions;
        _clock = clock;
    }

    public override string Title => "Công việc hôm nay";

    public ObservableCollection<TaskItemViewModel> Ranked { get; } = new();
    public ObservableCollection<ScheduleBlockItem> Blocks { get; } = new();
    public ObservableCollection<WarningItem> Warnings { get; } = new();
    public ObservableCollection<TaskItemViewModel> Waiting { get; } = new();

    [ObservableProperty] private string _dateText = "";
    [ObservableProperty] private string _scheduleSummary = "";
    [ObservableProperty] private bool _showAllDirect;

    partial void OnShowAllDirectChanged(bool value) => _ = RefreshAsync();

    public override async Task RefreshAsync()
    {
        IsLoading = true;
        try
        {
            var now = _clock.Now;
            var settings = _settings.Current;
            DateText = $"{Labels.DayOfWeekVi(now.DayOfWeek)}, {now:dd/MM/yyyy} — {now:HH:mm}";
            var open = await _tasks.GetOpenTasksAsync();
            var engine = new PriorityEngine(settings);

            var ranked = engine.RankDirectWork(open, now);
            var soon = now.Date.AddDays(settings.DueSoonDays + 1);
            var relevant = ShowAllDirect
                ? ranked
                : ranked.Where(a => a.Task.DueAt < soon ||
                                    (a.Task.PlannedStart.HasValue && a.Task.PlannedStart.Value.Date <= now.Date) ||
                                    a.Task.Status == WorkTaskStatus.InProgress ||
                                    a.TimeRisk).ToList();
            Ranked.Clear();
            foreach (var a in relevant) Ranked.Add(new TaskItemViewModel(a.Task, now, _actions, a));

            var proposal = new ScheduleEngine(settings).Propose(open, now, horizonDays: 30);
            Blocks.Clear();
            foreach (var b in proposal.BlocksOn(now.Date))
            {
                var item = new TaskItemViewModel(b.Task, now, _actions, engine.Assess(b.Task, now));
                var block = b;
                Blocks.Add(new ScheduleBlockItem
                {
                    Block = b,
                    Item = item,
                    ApplyCommand = new AsyncRelayCommand(() => ApplyAsync(block), () => !block.IsFixed)
                });
            }

            Warnings.Clear();
            foreach (var w in proposal.Warnings.OrderByDescending(w => w.IsCertain).ThenBy(w => w.Task.DueAt))
                Warnings.Add(new WarningItem { Message = w.Message, IsCertain = w.IsCertain, Item = new TaskItemViewModel(w.Task, now, _actions) });

            var hours = Blocks.Where(b => !b.IsFixed).Sum(b => b.Block.Hours);
            var free = new WorkCalendar(settings).WorkingHoursBetween(now, now.Date.AddDays(1)) - Blocks.Sum(b => b.Block.Hours);
            ScheduleSummary = !settings.IsWorkDay(now)
                ? "Hôm nay không phải ngày làm việc theo cấu hình."
                : Blocks.Count == 0
                    ? "Không còn khung giờ làm việc trống hoặc không có công việc cần xếp lịch hôm nay."
                    : $"Đề xuất {hours:0.#} giờ xử lý công việc; còn trống khoảng {Math.Max(0, free):0.#} giờ. Khối có nhãn \"ước tính\" dùng thời lượng mặc định {settings.DefaultEstimateHours:0.#} giờ.";

            Waiting.Clear();
            foreach (var a in engine.RankWaiting(open, now)) Waiting.Add(new TaskItemViewModel(a.Task, now, _actions, a));
        }
        finally { IsLoading = false; }
    }

    private async Task ApplyAsync(ScheduledBlock block)
    {
        await _tasks.SetPlannedStartAsync(block.Task.Id, block.Start);
    }

    [RelayCommand]
    private async Task ApplyAll()
    {
        // Chỉ ghi thời điểm bắt đầu cho khối đầu tiên của mỗi công việc trong hôm nay
        foreach (var g in Blocks.Where(b => !b.IsFixed && !b.IsApplied).GroupBy(b => b.Block.Task.Id))
            await _tasks.SetPlannedStartAsync(g.Key, g.Min(b => b.Block.Start));
    }

    [RelayCommand]
    private Task AddTask() => _actions.AddNewAsync(_clock.Now.Date);
}
