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

public sealed record Choice<T>(string Label, T Value)
{
    public override string ToString() => Label;
}

public sealed record StatusChoice(string Label, StatusFilter Filter, WorkTaskStatus? Status)
{
    public override string ToString() => Label;
}

public sealed record PriorityChoice(string Label, TaskPriority? Exact, TaskPriority? Min)
{
    public override string ToString() => Label;
}

/// <summary>Tất cả công việc: lọc, tìm kiếm, sắp xếp nhiều tiêu chí, phân trang, thùng rác, xuất Excel.</summary>
public sealed partial class TaskListViewModel : PageViewModel
{
    private readonly TaskService _tasks;
    private readonly CategoryService _categories;
    private readonly SettingsService _settings;
    private readonly TaskActions _actions;
    private readonly DialogService _dialogs;
    private readonly ExcelExportService _excel;
    private readonly IClock _clock;
    private bool _suspend;
    private bool _categoriesLoaded;

    public TaskListViewModel(TaskService tasks, CategoryService categories, SettingsService settings, TaskActions actions,
        DialogService dialogs, ExcelExportService excel, IClock clock)
    {
        _tasks = tasks;
        _categories = categories;
        _settings = settings;
        _actions = actions;
        _dialogs = dialogs;
        _excel = excel;
        _clock = clock;

        StatusChoices = new List<StatusChoice>
        {
            new("Chưa hoàn thành", StatusFilter.Open, null),
            new("Đang trực tiếp xử lý", StatusFilter.DirectWork, null),
            new("Chờ phối hợp/phản hồi/phê duyệt", StatusFilter.Waiting, null),
            new("Tất cả trạng thái", StatusFilter.All, null),
        };
        StatusChoices.AddRange(Enum.GetValues<WorkTaskStatus>().Select(s => new StatusChoice("• " + Labels.Of(s), StatusFilter.Specific, s)));

        PriorityChoices = new List<PriorityChoice>
        {
            new("Mọi mức ưu tiên", null, null),
            new("Cao và Khẩn cấp", null, TaskPriority.High),
            new("Khẩn cấp", TaskPriority.Urgent, null),
            new("Cao", TaskPriority.High, null),
            new("Bình thường", TaskPriority.Normal, null),
            new("Thấp", TaskPriority.Low, null),
        };

        TimeChoices = new List<Choice<TimeFilter>>
        {
            new("Mọi thời hạn", TimeFilter.All),
            new("Quá hạn", TimeFilter.Overdue),
            new("Hôm nay", TimeFilter.Today),
            new("Sắp đến hạn", TimeFilter.DueSoon),
            new("Hạn trong tuần này", TimeFilter.ThisWeek),
            new("Hạn tuần sau", TimeFilter.NextWeek),
            new("Hạn trong tháng này", TimeFilter.ThisMonth),
            new("Nhận trong tuần này", TimeFilter.ReceivedThisWeek),
            new("Hoàn thành trong tuần này", TimeFilter.CompletedThisWeek),
        };

        SortChoices = new List<Choice<SortField>>
        {
            new("Thứ tự ưu tiên đề xuất", SortField.Suggested),
            new("Hạn hoàn thành", SortField.DueAt),
            new("Mức ưu tiên", SortField.Priority),
            new("Ngày nhận", SortField.ReceivedAt),
            new("Trạng thái", SortField.Status),
            new("Mã công việc", SortField.Code),
            new("Nội dung", SortField.Title),
            new("Cập nhật gần nhất", SortField.UpdatedAt),
        };
        ThenSortChoices = new List<Choice<SortField?>> { new("(không)", null) };
        ThenSortChoices.AddRange(SortChoices.Where(s => s.Value != SortField.Suggested).Select(s => new Choice<SortField?>(s.Label, s.Value)));

        PageSizes = new List<int> { 50, 100, 200, 500 };

        _selectedStatus = StatusChoices[0];
        _selectedPriority = PriorityChoices[0];
        _selectedTime = TimeChoices[0];
        _selectedSort = SortChoices[0];
        _selectedThenSort = ThenSortChoices[0];
        _selectedGroup = new Choice<int?>("Mọi nhóm công việc", null);
        GroupChoices.Add(_selectedGroup);
    }

    public override string Title => IsTrash ? "Thùng rác" : "Tất cả công việc";

    public List<StatusChoice> StatusChoices { get; }
    public List<PriorityChoice> PriorityChoices { get; }
    public List<Choice<TimeFilter>> TimeChoices { get; }
    public List<Choice<SortField>> SortChoices { get; }
    public List<Choice<SortField?>> ThenSortChoices { get; }
    public ObservableCollection<Choice<int?>> GroupChoices { get; } = new();
    public List<int> PageSizes { get; }

    public ObservableCollection<TaskItemViewModel> Items { get; } = new();
    public List<TaskItemViewModel> SelectedItems { get; } = new();

    [ObservableProperty] private string _search = "";
    [ObservableProperty] private StatusChoice _selectedStatus;
    [ObservableProperty] private PriorityChoice _selectedPriority;
    [ObservableProperty] private Choice<TimeFilter> _selectedTime;
    [ObservableProperty] private Choice<int?> _selectedGroup;
    [ObservableProperty] private Choice<SortField> _selectedSort;
    [ObservableProperty] private bool _sortDescending;
    [ObservableProperty] private Choice<SortField?> _selectedThenSort;
    [ObservableProperty] private bool _thenSortDescending;
    [ObservableProperty] private int _pageSize = 100;
    [ObservableProperty] private int _page = 1;
    [ObservableProperty] private int _pageCount = 1;
    [ObservableProperty] private int _total;
    [ObservableProperty] private bool _isTrash;
    [ObservableProperty] private int _trashCount;
    [ObservableProperty] private string _summary = "";

    partial void OnSearchChanged(string value) => Requery();
    partial void OnSelectedStatusChanged(StatusChoice value) => Requery();
    partial void OnSelectedPriorityChanged(PriorityChoice value) => Requery();
    partial void OnSelectedTimeChanged(Choice<TimeFilter> value) => Requery();
    partial void OnSelectedGroupChanged(Choice<int?> value) => Requery();
    partial void OnSelectedSortChanged(Choice<SortField> value) => Requery();
    partial void OnSortDescendingChanged(bool value) => Requery();
    partial void OnSelectedThenSortChanged(Choice<SortField?> value) => Requery();
    partial void OnThenSortDescendingChanged(bool value) => Requery();
    partial void OnPageSizeChanged(int value) => Requery();
    partial void OnIsTrashChanged(bool value)
    {
        OnPropertyChanged(nameof(Title));
        Requery();
    }

    private CancellationTokenSource? _debounce;

    private void Requery(bool resetPage = true)
    {
        if (_suspend) return;
        if (resetPage) _page = 1;
        OnPropertyChanged(nameof(Page));
        _debounce?.Cancel();
        var cts = _debounce = new CancellationTokenSource();
        _ = Task.Delay(250, cts.Token).ContinueWith(t =>
        {
            if (!t.IsCanceled) _ = RefreshAsync();
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    /// <summary>Áp dụng bộ lọc định sẵn (từ Tổng quan hoặc ô tìm kiếm).</summary>
    public void Apply(TaskListRequest r)
    {
        _suspend = true;
        try
        {
            IsTrash = false;
            Search = r.Search ?? "";
            SelectedStatus = r.Status == StatusFilter.Specific
                ? StatusChoices.First(s => s.Status == r.SpecificStatus)
                : StatusChoices.First(s => s.Filter == r.Status && s.Status == null);
            SelectedPriority = r.HighOrUrgent ? PriorityChoices[1]
                : r.Priority.HasValue ? PriorityChoices.First(p => p.Exact == r.Priority) : PriorityChoices[0];
            SelectedTime = TimeChoices.First(t => t.Value == r.Time);
            SelectedGroup = GroupChoices[0];
            _page = 1;
        }
        finally { _suspend = false; }
    }

    private async Task EnsureCategoriesAsync()
    {
        if (_categoriesLoaded) return;
        _categoriesLoaded = true;
        foreach (var g in (await _categories.GetAllAsync()).Where(c => c.ParentId == null))
            GroupChoices.Add(new Choice<int?>(g.Name, g.Id));
    }

    public void InvalidateCategories()
    {
        _categoriesLoaded = false;
        while (GroupChoices.Count > 1) GroupChoices.RemoveAt(1);
        _selectedGroup = GroupChoices[0];
        OnPropertyChanged(nameof(SelectedGroup));
    }

    private TaskQuery BuildQuery() => new()
    {
        Search = Search,
        StatusFilter = SelectedStatus.Filter,
        Status = SelectedStatus.Status,
        Priority = SelectedPriority.Exact,
        MinPriority = SelectedPriority.Min,
        Time = SelectedTime.Value,
        CategoryGroupId = SelectedGroup?.Value,
        Deleted = IsTrash,
        Sort = SelectedSort.Value,
        SortDescending = SortDescending,
        ThenSort = SelectedThenSort.Value,
        ThenSortDescending = ThenSortDescending,
        Page = Page,
        PageSize = PageSize
    };

    public override async Task RefreshAsync()
    {
        IsLoading = true;
        try
        {
            await EnsureCategoriesAsync();
            var now = _clock.Now;
            var result = await _tasks.QueryAsync(BuildQuery());
            if (result.Items.Count == 0 && result.Total > 0 && Page > 1)
            {
                _page = result.PageCount;
                OnPropertyChanged(nameof(Page));
                result = await _tasks.QueryAsync(BuildQuery());
            }
            var engine = new PriorityEngine(_settings.Current);
            Items.Clear();
            foreach (var t in result.Items)
                Items.Add(new TaskItemViewModel(t, now, _actions, StatusGroups.IsClosed(t.Status) ? null : engine.Assess(t, now)));
            Total = result.Total;
            PageCount = result.PageCount;
            TrashCount = await _tasks.CountTrashAsync();
            var from = result.Total == 0 ? 0 : (result.Page - 1) * result.PageSize + 1;
            var to = (result.Page - 1) * result.PageSize + result.Items.Count;
            Summary = result.Total == 0
                ? (IsTrash ? "Thùng rác trống." : "Không có công việc phù hợp bộ lọc.")
                : $"Hiển thị {from:N0}–{to:N0} / {result.Total:N0} công việc";
            SelectedItems.Clear();
        }
        catch (Exception ex) { _dialogs.Error("Không tải được danh sách công việc.", ex); }
        finally { IsLoading = false; }
    }

    // ---------------- Phân trang
    [RelayCommand] private Task FirstPage() { Page = 1; return RefreshAsync(); }
    [RelayCommand] private Task PrevPage() { if (Page > 1) Page--; return RefreshAsync(); }
    [RelayCommand] private Task NextPage() { if (Page < PageCount) Page++; return RefreshAsync(); }
    [RelayCommand] private Task LastPage() { Page = PageCount; return RefreshAsync(); }

    [RelayCommand]
    private void ClearFilters()
    {
        Apply(new TaskListRequest());
        Requery();
    }

    // ---------------- Thao tác
    private TaskItemViewModel? Current => SelectedItems.FirstOrDefault();

    [RelayCommand] private Task Add() => _actions.AddNewAsync();

    [RelayCommand]
    private async Task Edit()
    {
        if (Current == null) { _dialogs.Info("Hãy chọn một công việc."); return; }
        await _actions.EditAsync(Current.Id);
    }

    [RelayCommand]
    private async Task Duplicate()
    {
        if (Current == null) { _dialogs.Info("Hãy chọn một công việc để sao chép."); return; }
        await _actions.DuplicateAsync(Current);
    }

    [RelayCommand]
    private async Task Complete()
    {
        if (Current == null) { _dialogs.Info("Hãy chọn công việc cần ghi nhận hoàn thành."); return; }
        await _actions.CompleteAsync(Current);
    }

    [RelayCommand]
    private async Task Delete()
    {
        if (SelectedItems.Count == 0) { _dialogs.Info("Hãy chọn công việc cần xóa."); return; }
        await _actions.MoveToTrashAsync(SelectedItems.ToList());
    }

    [RelayCommand]
    private async Task Restore()
    {
        if (SelectedItems.Count == 0) { _dialogs.Info("Hãy chọn công việc cần khôi phục."); return; }
        try { await _tasks.RestoreAsync(SelectedItems.Select(i => i.Id).ToList()); }
        catch (Exception ex) { _dialogs.Error("Không khôi phục được.", ex); }
    }

    [RelayCommand]
    private async Task Purge()
    {
        if (SelectedItems.Count == 0) { _dialogs.Info("Hãy chọn công việc cần xóa vĩnh viễn."); return; }
        if (!_dialogs.Confirm($"XÓA VĨNH VIỄN {SelectedItems.Count} công việc đã chọn?\n\nThao tác này KHÔNG THỂ khôi phục (trừ khi phục hồi từ bản sao lưu).")) return;
        try { await _tasks.PurgeAsync(SelectedItems.Select(i => i.Id).ToList()); }
        catch (Exception ex) { _dialogs.Error("Không xóa được.", ex); }
    }

    [RelayCommand]
    private void ToggleTrash() => IsTrash = !IsTrash;

    [RelayCommand]
    private async Task ExportExcel()
    {
        var path = _dialogs.SaveFile("Excel (*.xlsx)|*.xlsx", $"DanhSachCongViec_{_clock.Now:yyyyMMdd_HHmm}.xlsx");
        if (path == null) return;
        try
        {
            IsLoading = true;
            var all = await _tasks.QueryAllAsync(BuildQuery());
            await _excel.ExportTasksAsync(all, path, _clock.Now, IsTrash ? "Công việc trong thùng rác" : "Danh sách công việc");
            if (_dialogs.Confirm($"Đã xuất {all.Count:N0} công việc ra tệp:\n{path}\n\nMở tệp ngay?")) _dialogs.OpenPath(path);
        }
        catch (Exception ex) { _dialogs.Error("Không xuất được Excel (tệp có thể đang mở trong Excel).", ex); }
        finally { IsLoading = false; }
    }
}
