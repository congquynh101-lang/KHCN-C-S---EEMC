using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EEMC.WM.App.Infrastructure;
using EEMC.WM.Core.Domain;
using EEMC.WM.Core.Services;
using EEMC.WM.Core.Text;
using EEMC.WM.Core.Time;
using EEMC.WM.Data.Services;

namespace EEMC.WM.App.ViewModels;

public enum CalendarMode { Day, Week, Month }

/// <summary>Một mục trên lịch: hạn hoàn thành, lịch cố định hoặc thời điểm bắt đầu theo kế hoạch.</summary>
public sealed class CalendarEntry
{
    public required TaskItemViewModel Item { get; init; }
    public required DateTime At { get; init; }
    /// <summary>"Hạn", "Cố định", "Kế hoạch".</summary>
    public required string Kind { get; init; }
    public string TimeText => At.ToString("HH:mm");
    public string Color => Item.IsOverdue ? "Red" : Item.IsClosed ? "Green" : Kind == "Hạn" ? Item.PriorityColor : "Blue";
    public string Tooltip => $"{Kind} {Labels.DateTime(At)}\n[{Item.Code}] {Item.Title}\n{Item.StatusText} • Ưu tiên {Item.PriorityText}\n{Item.RemainingText}";
}

public sealed class CalendarDay
{
    public required DateTime Date { get; init; }
    public bool IsToday { get; init; }
    public bool IsOtherMonth { get; init; }
    public bool IsWorkDay { get; init; }
    public string DayNumber => Date.Day.ToString();
    public string Header => $"{Labels.DayOfWeekShort(Date.DayOfWeek)} {Date:dd/MM}";
    public List<CalendarEntry> Entries { get; init; } = new();
    public List<CalendarEntry> Visible { get; init; } = new();
    public int MoreCount => Entries.Count - Visible.Count;
    public string MoreText => MoreCount > 0 ? $"+{MoreCount} công việc khác" : "";
    public double PlannedHours { get; init; }
    public string LoadText => PlannedHours > 0 ? $"Đề xuất {PlannedHours:0.#} giờ" : "";
}

/// <summary>Lịch làm việc ngày/tuần/tháng. Kéo thả công việc sang ngày khác để đổi hạn hoặc đặt ngày thực hiện.</summary>
public sealed partial class CalendarViewModel : PageViewModel
{
    private readonly TaskService _tasks;
    private readonly SettingsService _settings;
    private readonly TaskActions _actions;
    private readonly DialogService _dialogs;
    private readonly IClock _clock;

    public CalendarViewModel(TaskService tasks, SettingsService settings, TaskActions actions, DialogService dialogs, IClock clock)
    {
        _tasks = tasks;
        _settings = settings;
        _actions = actions;
        _dialogs = dialogs;
        _clock = clock;
        _anchor = clock.Now.Date;
    }

    public override string Title => "Lịch làm việc";

    [ObservableProperty] private CalendarMode _mode = CalendarMode.Week;
    [ObservableProperty] private DateTime _anchor;
    [ObservableProperty] private string _rangeText = "";
    [ObservableProperty] private CalendarDay? _selectedDay;

    public ObservableCollection<CalendarDay> Days { get; } = new();
    /// <summary>Chế độ ngày: lịch đề xuất (khối thời gian) trong ngày.</summary>
    public ObservableCollection<ScheduleBlockItem> DayBlocks { get; } = new();
    public ObservableCollection<CalendarEntry> DayEntries { get; } = new();

    public bool IsDay => Mode == CalendarMode.Day;
    public bool IsWeek => Mode == CalendarMode.Week;
    public bool IsMonth => Mode == CalendarMode.Month;

    partial void OnModeChanged(CalendarMode value)
    {
        OnPropertyChanged(nameof(IsDay));
        OnPropertyChanged(nameof(IsWeek));
        OnPropertyChanged(nameof(IsMonth));
        _ = RefreshAsync();
    }

    [RelayCommand] private void SetMode(string mode) => Mode = Enum.Parse<CalendarMode>(mode);

    [RelayCommand]
    private Task Prev()
    {
        Anchor = Mode switch { CalendarMode.Day => Anchor.AddDays(-1), CalendarMode.Week => Anchor.AddDays(-7), _ => Anchor.AddMonths(-1) };
        return RefreshAsync();
    }

    [RelayCommand]
    private Task Next()
    {
        Anchor = Mode switch { CalendarMode.Day => Anchor.AddDays(1), CalendarMode.Week => Anchor.AddDays(7), _ => Anchor.AddMonths(1) };
        return RefreshAsync();
    }

    [RelayCommand]
    private Task Today()
    {
        Anchor = _clock.Now.Date;
        return RefreshAsync();
    }

    [RelayCommand]
    private Task OpenDay(CalendarDay? day)
    {
        if (day == null) return Task.CompletedTask;
        Anchor = day.Date;
        Mode = CalendarMode.Day; // OnModeChanged sẽ làm mới
        return Mode == CalendarMode.Day ? RefreshAsync() : Task.CompletedTask;
    }

    [RelayCommand]
    private Task AddOnDay(CalendarDay? day) => _actions.AddNewAsync((day?.Date) ?? Anchor);

    public override async Task RefreshAsync()
    {
        IsLoading = true;
        try
        {
            var now = _clock.Now;
            var settings = _settings.Current;
            DateTime from, to;
            switch (Mode)
            {
                case CalendarMode.Day:
                    from = Anchor.Date; to = from.AddDays(1);
                    RangeText = $"{Labels.DayOfWeekVi(from.DayOfWeek)}, {from:dd/MM/yyyy}";
                    break;
                case CalendarMode.Week:
                    from = TaskService.StartOfWeek(Anchor); to = from.AddDays(7);
                    RangeText = $"Tuần {System.Globalization.ISOWeek.GetWeekOfYear(from)}: {from:dd/MM} – {to.AddDays(-1):dd/MM/yyyy}";
                    break;
                default:
                    var first = new DateTime(Anchor.Year, Anchor.Month, 1);
                    from = TaskService.StartOfWeek(first); to = from.AddDays(42);
                    RangeText = $"Tháng {Anchor.Month}/{Anchor.Year}";
                    break;
            }

            var tasks = await _tasks.GetTasksInRangeAsync(from, to);
            var engine = new PriorityEngine(settings);
            var entries = new List<CalendarEntry>();
            foreach (var t in tasks)
            {
                var item = new TaskItemViewModel(t, now, _actions, StatusGroups.IsClosed(t.Status) ? null : engine.Assess(t, now));
                if (t.DueAt >= from && t.DueAt < to) entries.Add(new CalendarEntry { Item = item, At = t.DueAt, Kind = "Hạn" });
                if (t.PlannedStart.HasValue && t.PlannedStart >= from && t.PlannedStart < to && t.PlannedStart.Value.Date != t.DueAt.Date)
                    entries.Add(new CalendarEntry { Item = item, At = t.PlannedStart.Value, Kind = t.IsFixedTime ? "Cố định" : "Kế hoạch" });
                else if (t.PlannedStart.HasValue && t.IsFixedTime && t.PlannedStart.Value.Date == t.DueAt.Date && t.PlannedStart >= from && t.PlannedStart < to)
                    entries.Add(new CalendarEntry { Item = item, At = t.PlannedStart.Value, Kind = "Cố định" });
            }

            // Lịch đề xuất chỉ tính cho tương lai
            ScheduleProposal? proposal = null;
            if (to > now)
            {
                var open = await _tasks.GetOpenTasksAsync();
                var days = Math.Max(1, (int)Math.Ceiling((to - now.Date).TotalDays));
                proposal = new ScheduleEngine(settings).Propose(open, now, Math.Min(days, 60));
            }

            int perCell = Mode == CalendarMode.Month ? 3 : 50;
            Days.Clear();
            for (var d = from; d < to; d = d.AddDays(1))
            {
                var list = entries.Where(e => e.At.Date == d).OrderBy(e => e.At).ThenByDescending(e => e.Item.Task.Priority).ToList();
                Days.Add(new CalendarDay
                {
                    Date = d,
                    IsToday = d == now.Date,
                    IsOtherMonth = Mode == CalendarMode.Month && d.Month != Anchor.Month,
                    IsWorkDay = settings.IsWorkDay(d),
                    Entries = list,
                    Visible = list.Take(perCell).ToList(),
                    PlannedHours = proposal?.BlocksOn(d).Where(b => !b.IsFixed).Sum(b => b.Hours) ?? 0
                });
            }

            DayEntries.Clear();
            DayBlocks.Clear();
            if (Mode == CalendarMode.Day)
            {
                foreach (var e in entries.Where(e => e.At.Date == from).OrderBy(e => e.At)) DayEntries.Add(e);
                if (proposal != null)
                {
                    foreach (var b in proposal.BlocksOn(from))
                    {
                        var block = b;
                        DayBlocks.Add(new ScheduleBlockItem
                        {
                            Block = b,
                            Item = new TaskItemViewModel(b.Task, now, _actions, engine.Assess(b.Task, now)),
                            ApplyCommand = new AsyncRelayCommand(() => _tasks.SetPlannedStartAsync(block.Task.Id, block.Start), () => !block.IsFixed)
                        });
                    }
                }
            }
        }
        catch (Exception ex) { _dialogs.Error("Không tải được lịch.", ex); }
        finally { IsLoading = false; }
    }

    /// <summary>Kéo thả công việc sang ngày khác.</summary>
    public async Task MoveAsync(CalendarEntry entry, DateTime targetDate)
    {
        var t = entry.Item.Task;
        if (entry.At.Date == targetDate.Date) return;
        if (StatusGroups.IsClosed(t.Status))
        {
            _dialogs.Info("Công việc đã hoàn thành/hủy, không thể dời lịch.");
            return;
        }
        var newAt = targetDate.Date + entry.At.TimeOfDay;
        var r = _dialogs.YesNoCancel(
            $"Dời công việc sang {Labels.DayOfWeekVi(targetDate.DayOfWeek)} {targetDate:dd/MM/yyyy}?\n\n[{t.Code}] {t.Title}\n\n" +
            $"• Có (Yes): ĐỔI HẠN HOÀN THÀNH thành {Labels.DateTime(newAt)}\n" +
            $"• Không (No): giữ nguyên hạn, ĐẶT NGÀY THỰC HIỆN theo kế hoạch vào {targetDate:dd/MM/yyyy}\n" +
            "• Hủy (Cancel): không thay đổi");
        if (r == MessageBoxResult.Cancel) return;
        try
        {
            var full = await _tasks.GetAsync(t.Id);
            if (full == null) return;
            if (r == MessageBoxResult.Yes)
            {
                if (newAt < full.ReceivedAt) { _dialogs.Warn("Hạn mới không được trước ngày nhận việc."); return; }
                full.DueAt = newAt;
                full.Category = null;
                await _tasks.UpdateAsync(full);
            }
            else
            {
                var planned = entry.Kind == "Hạn" ? targetDate.Date + (_settings.Current.WorkPeriods.Count > 0 ? _settings.Current.WorkPeriods.Min(p => p.Start) : TimeSpan.FromHours(8)) : newAt;
                await _tasks.SetPlannedStartAsync(t.Id, planned);
            }
        }
        catch (Exception ex) { _dialogs.Error("Không dời được công việc.", ex); }
    }
}
