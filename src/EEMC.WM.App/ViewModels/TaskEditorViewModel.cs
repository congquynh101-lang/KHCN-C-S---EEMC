using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EEMC.WM.App.Infrastructure;
using EEMC.WM.Core.Domain;
using EEMC.WM.Core.Services;
using EEMC.WM.Core.Text;
using EEMC.WM.Core.Time;
using EEMC.WM.Data.Services;

namespace EEMC.WM.App.ViewModels;

public sealed record CategoryOption(int? Id, string Display, bool IsGroup)
{
    public override string ToString() => Display.Trim();
}

public sealed record DocumentStatusOption(DocumentStatus Value, string Label)
{
    public static readonly IReadOnlyList<DocumentStatusOption> All =
        Enum.GetValues<DocumentStatus>().Select(d => new DocumentStatusOption(d, Labels.Of(d))).ToList();
    public override string ToString() => Label;
}

public sealed class HistoryItem
{
    public HistoryItem(TaskHistory h) { H = h; }
    public TaskHistory H { get; }
    public string AtText => Labels.DateTime(H.At);
    public string Action => H.Action;
    public string Detail => H.Detail;
    public bool IsComment => H.IsComment;
}

/// <summary>Thêm/sửa công việc. Chỉ 4 trường bắt buộc; còn lại nằm trong "Thông tin mở rộng".</summary>
public sealed partial class TaskEditorViewModel : ObservableObject
{
    private readonly TaskService _tasks;
    private readonly CategoryService _categories;
    private readonly SettingsService _settings;
    private readonly DialogService _dialogs;
    private readonly IClock _clock;
    private WorkTask _original = new();

    public TaskEditorViewModel(TaskService tasks, CategoryService categories, SettingsService settings, DialogService dialogs, IClock clock)
    {
        _tasks = tasks;
        _categories = categories;
        _settings = settings;
        _dialogs = dialogs;
        _clock = clock;
    }

    public event Action<bool>? RequestClose;

    [ObservableProperty] private bool _isNew = true;
    [ObservableProperty] private string _header = "Thêm công việc";
    [ObservableProperty] private string _code = "";

    // 4 trường bắt buộc
    [ObservableProperty] private DateTime? _receivedAt;
    [ObservableProperty] private DateTime? _dueAt;
    [ObservableProperty] private string _title = "";
    [ObservableProperty] private TaskPriority _priority = TaskPriority.Normal;

    [ObservableProperty] private WorkTaskStatus _status = WorkTaskStatus.NotStarted;
    [ObservableProperty] private CategoryOption? _category;
    [ObservableProperty] private string? _projectName;
    [ObservableProperty] private string? _assignedBy;
    [ObservableProperty] private string? _assignee;
    [ObservableProperty] private string? _coordinator;
    [ObservableProperty] private string _estimatedHoursText = "";
    [ObservableProperty] private DateTime? _plannedStart;
    [ObservableProperty] private bool _isFixedTime;
    [ObservableProperty] private int _progress;
    [ObservableProperty] private string? _deliverable;
    [ObservableProperty] private DocumentStatusOption _documentStatus = DocumentStatusOption.All[0];
    [ObservableProperty] private string? _documentNumber;
    [ObservableProperty] private string? _leaderOpinionNeeded;
    [ObservableProperty] private string? _notes;
    [ObservableProperty] private string? _folderPath;
    [ObservableProperty] private DateTime? _expectedResponseAt;

    [ObservableProperty] private bool _showExtended;
    [ObservableProperty] private string _errorText = "";
    [ObservableProperty] private string _assessmentText = "";
    [ObservableProperty] private string _suggestionText = "";
    [ObservableProperty] private string _newComment = "";
    [ObservableProperty] private bool _isBusy;

    public ObservableCollection<CategoryOption> Categories { get; } = new();
    public ObservableCollection<HistoryItem> History { get; } = new();
    public IReadOnlyList<StatusOption> Statuses => StatusOption.All;
    public IReadOnlyList<DocumentStatusOption> DocumentStatuses => DocumentStatusOption.All;

    public bool IsWaitingStatus => StatusGroups.IsWaiting(Status);
    public string RemainingText => DueAt.HasValue ? Labels.TimeRemaining(DueAt.Value, _clock.Now) : "";

    partial void OnStatusChanged(WorkTaskStatus value) => OnPropertyChanged(nameof(IsWaitingStatus));
    partial void OnDueAtChanged(DateTime? value) { OnPropertyChanged(nameof(RemainingText)); UpdateAssessment(); }
    partial void OnPriorityChanged(TaskPriority value) => UpdateAssessment();
    partial void OnEstimatedHoursTextChanged(string value) => UpdateAssessment();

    private async Task LoadCategoriesAsync(int? selectedId)
    {
        Categories.Clear();
        Categories.Add(new CategoryOption(null, "(Chưa phân nhóm)", false));
        foreach (var c in await _categories.GetAllAsync(includeInactive: false))
            Categories.Add(new CategoryOption(c.Id, c.ParentId == null ? c.Name : "      " + c.Name, c.ParentId == null));
        if (selectedId.HasValue && Categories.All(c => c.Id != selectedId))
        {
            // Danh mục đã ngừng dùng nhưng công việc vẫn đang gắn: vẫn hiển thị
            var all = await _categories.GetAllAsync(includeInactive: true);
            var cat = all.FirstOrDefault(c => c.Id == selectedId);
            if (cat != null) Categories.Add(new CategoryOption(cat.Id, cat.Name + " (ngừng dùng)", false));
        }
        Category = Categories.FirstOrDefault(c => c.Id == selectedId) ?? Categories[0];
    }

    public async Task InitializeNewAsync(DateTime? dueDate)
    {
        IsNew = true;
        Header = "Thêm công việc mới";
        var now = _clock.Now;
        ReceivedAt = now;
        DueAt = dueDate.HasValue ? dueDate.Value.Date + DefaultDueTime : null;
        Priority = TaskPriority.Normal;
        Status = WorkTaskStatus.NotStarted;
        await LoadCategoriesAsync(null);
        UpdateAssessment();
    }

    public async Task<bool> InitializeEditAsync(int id)
    {
        var t = await _tasks.GetAsync(id);
        if (t == null) return false;
        _original = t;
        IsNew = false;
        Header = "Cập nhật công việc";
        Code = t.Code;
        ReceivedAt = t.ReceivedAt;
        DueAt = t.DueAt;
        Title = t.Title;
        Priority = t.Priority;
        Status = t.Status;
        ProjectName = t.ProjectName;
        AssignedBy = t.AssignedBy;
        Assignee = t.Assignee;
        Coordinator = t.Coordinator;
        EstimatedHoursText = t.EstimatedHours?.ToString("0.##", CultureInfo.GetCultureInfo("vi-VN")) ?? "";
        PlannedStart = t.PlannedStart;
        IsFixedTime = t.IsFixedTime;
        Progress = t.Progress;
        Deliverable = t.Deliverable;
        DocumentStatus = DocumentStatusOption.All.First(d => d.Value == t.DocumentStatus);
        DocumentNumber = t.DocumentNumber;
        LeaderOpinionNeeded = t.LeaderOpinionNeeded;
        Notes = t.Notes;
        FolderPath = t.FolderPath;
        ExpectedResponseAt = t.ExpectedResponseAt;
        await LoadCategoriesAsync(t.CategoryId);
        ShowExtended = true;
        await LoadHistoryAsync();
        UpdateAssessment();
        return true;
    }

    private async Task LoadHistoryAsync()
    {
        History.Clear();
        if (IsNew) return;
        foreach (var h in await _tasks.GetHistoryAsync(_original.Id)) History.Add(new HistoryItem(h));
    }

    private TimeSpan DefaultDueTime
    {
        get
        {
            var periods = _settings.Current.WorkPeriods;
            return periods.Count > 0 ? periods.Max(p => p.End) : new TimeSpan(17, 0, 0);
        }
    }

    [RelayCommand]
    private void SetDue(string mode)
    {
        var now = _clock.Now;
        var s = _settings.Current;
        DateTime NextWorkDay(DateTime d)
        {
            d = d.AddDays(1);
            for (int i = 0; i < 14 && !s.IsWorkDay(d); i++) d = d.AddDays(1);
            return d;
        }
        DateTime target = mode switch
        {
            "today" => now.Date,
            "tomorrow" => NextWorkDay(now.Date),
            "3d" => NextWorkDay(NextWorkDay(NextWorkDay(now.Date))),
            "friday" => now.Date.AddDays(((int)DayOfWeek.Friday - (int)now.DayOfWeek + 7) % 7),
            "nextweek" => TaskService.StartOfWeek(now).AddDays(7 + 4),
            _ => now.Date
        };
        DueAt = target.Date + DefaultDueTime;
    }

    [RelayCommand]
    private void ReceivedNow() => ReceivedAt = _clock.Now;

    [RelayCommand]
    private void BrowseFolder()
    {
        var f = _dialogs.PickFolder(FolderPath);
        if (f != null) FolderPath = f;
    }

    [RelayCommand]
    private void OpenFolder() => _dialogs.OpenPath(FolderPath);

    private bool TryParseEstimate(out double? value)
    {
        value = null;
        var s = (EstimatedHoursText ?? "").Trim().Replace(',', '.');
        if (s.Length == 0) return true;
        if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && v >= 0) { value = v; return true; }
        return false;
    }

    private WorkTask BuildTask()
    {
        TryParseEstimate(out var est);
        return new WorkTask
        {
            Id = _original.Id,
            Code = _original.Code,
            ReceivedAt = ReceivedAt ?? default,
            DueAt = DueAt ?? default,
            Title = (Title ?? "").Trim(),
            Priority = Priority,
            Status = Status,
            CategoryId = Category?.Id,
            ProjectName = Clean(ProjectName),
            AssignedBy = Clean(AssignedBy),
            Assignee = Clean(Assignee),
            Coordinator = Clean(Coordinator),
            EstimatedHours = est,
            PlannedStart = PlannedStart,
            IsFixedTime = IsFixedTime,
            Progress = Math.Clamp(Progress, 0, 100),
            Deliverable = Clean(Deliverable),
            DocumentStatus = DocumentStatus.Value,
            DocumentNumber = Clean(DocumentNumber),
            LeaderOpinionNeeded = Clean(LeaderOpinionNeeded),
            Notes = Clean(Notes),
            FolderPath = Clean(FolderPath),
            ExpectedResponseAt = ExpectedResponseAt,
            ParentId = _original.ParentId,
            ProjectId = _original.ProjectId,
            Weight = _original.Weight > 0 ? _original.Weight : 1,
            CreatedAt = IsNew ? _clock.Now : _original.CreatedAt
        };
    }

    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private void UpdateAssessment()
    {
        if (!DueAt.HasValue || ReceivedAt == null) { AssessmentText = ""; SuggestionText = ""; return; }
        var t = BuildTask();
        if (t.Id == 0) t.Id = -1;
        var a = new PriorityEngine(_settings.Current).Assess(t, _clock.Now);
        AssessmentText = $"{Labels.Of(a.Urgency)} • {Labels.Of(a.Quadrant)} • Điểm ưu tiên {a.Score:0}\n{a.Explanation}";
        SuggestionText = a.SuggestionDiffers
            ? $"Hệ thống đề xuất mức ưu tiên \"{Labels.Of(a.SuggestedPriority)}\" (chỉ tham khảo, không tự thay đổi lựa chọn của bạn)."
            : "";
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (IsBusy) return;
        ErrorText = "";
        if (!TryParseEstimate(out _))
        {
            ErrorText = "Thời lượng dự kiến phải là số giờ (ví dụ 2 hoặc 1,5).";
            ShowExtended = true;
            return;
        }
        var t = BuildTask();
        var errors = TaskValidator.Validate(t);
        if (errors.Count > 0)
        {
            ErrorText = string.Join(Environment.NewLine, errors.Select(e => "• " + e));
            return;
        }

        bool becomingCompleted = t.Status == WorkTaskStatus.Completed && (IsNew || _original.Status != WorkTaskStatus.Completed);
        if (becomingCompleted && !_dialogs.Confirm($"Xác nhận ghi nhận công việc đã HOÀN THÀNH?\n\n{t.Title}"))
            return;

        IsBusy = true;
        try
        {
            if (IsNew) await _tasks.CreateAsync(t);
            else await _tasks.UpdateAsync(t);
            RequestClose?.Invoke(true);
        }
        catch (ValidationException vex)
        {
            ErrorText = string.Join(Environment.NewLine, vex.Errors.Select(e => "• " + e));
        }
        catch (Exception ex)
        {
            _dialogs.Error("Không lưu được công việc.", ex);
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private void Cancel() => RequestClose?.Invoke(false);

    [RelayCommand]
    private async Task AddCommentAsync()
    {
        if (IsNew || string.IsNullOrWhiteSpace(NewComment)) return;
        try
        {
            await _tasks.AddCommentAsync(_original.Id, NewComment);
            NewComment = "";
            await LoadHistoryAsync();
        }
        catch (Exception ex) { _dialogs.Error("Không thêm được nội dung trao đổi.", ex); }
    }
}
