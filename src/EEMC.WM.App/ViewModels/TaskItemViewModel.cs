using CommunityToolkit.Mvvm.Input;
using EEMC.WM.App.Infrastructure;
using EEMC.WM.Core.Domain;
using EEMC.WM.Core.Services;
using EEMC.WM.Core.Text;

namespace EEMC.WM.App.ViewModels;

public sealed record StatusOption(WorkTaskStatus Status, string Label)
{
    public static readonly IReadOnlyList<StatusOption> All =
        Enum.GetValues<WorkTaskStatus>().Select(s => new StatusOption(s, Labels.Of(s))).ToList();
    public override string ToString() => Label;
}

public sealed record PriorityOption(TaskPriority Priority, string Label)
{
    public static readonly IReadOnlyList<PriorityOption> All =
        new[] { TaskPriority.Urgent, TaskPriority.High, TaskPriority.Normal, TaskPriority.Low }
            .Select(p => new PriorityOption(p, Labels.Of(p))).ToList();
    public override string ToString() => Label;
}

/// <summary>Một dòng công việc hiển thị trên các màn hình (danh sách, tổng quan, lịch).</summary>
public sealed class TaskItemViewModel
{
    private readonly TaskActions _actions;

    public TaskItemViewModel(WorkTask task, DateTime now, TaskActions actions, PriorityAssessment? assessment = null)
    {
        Task = task;
        Now = now;
        _actions = actions;
        Assessment = assessment;

        StartCommand = new AsyncRelayCommand(() => _actions.StartAsync(this), () => CanStart);
        CompleteCommand = new AsyncRelayCommand(() => _actions.CompleteAsync(this), () => !IsClosed);
        ChangeStatusCommand = new AsyncRelayCommand<StatusOption>(o => o == null ? System.Threading.Tasks.Task.CompletedTask : _actions.ChangeStatusAsync(this, o.Status));
        EditCommand = new AsyncRelayCommand(() => _actions.EditAsync(Id));
        OpenFolderCommand = new RelayCommand(() => _actions.OpenFolder(Task.FolderPath), () => HasFolder);
    }

    public WorkTask Task { get; }
    public DateTime Now { get; }
    public PriorityAssessment? Assessment { get; }

    public int Id => Task.Id;
    public string Code => Task.Code;
    public string Title => Task.Title;
    public bool IsClosed => StatusGroups.IsClosed(Task.Status);
    public bool IsOverdue => !IsClosed && Task.DueAt < Now;
    public bool CanStart => Task.Status != WorkTaskStatus.InProgress && !IsClosed;
    public bool HasFolder => !string.IsNullOrWhiteSpace(Task.FolderPath);

    public string ReceivedText => Labels.DateTime(Task.ReceivedAt);
    public string DueText => Labels.DateTime(Task.DueAt);
    public string DueTimeText => Task.DueAt.ToString("HH:mm");
    public string RemainingText => IsClosed
        ? (Task.Status == WorkTaskStatus.Completed && Task.CompletedAt.HasValue
            ? (Task.CompletedAt <= Task.DueAt ? "Hoàn thành đúng hạn" : "Hoàn thành trễ hạn")
            : "")
        : Labels.TimeRemaining(Task.DueAt, Now);

    public string PriorityText => Labels.Of(Task.Priority);
    public string StatusText => Labels.Of(Task.Status);
    public string ProgressText => $"{Task.Progress}%";
    public string CategoryText => Task.Category?.DisplayName ?? "";
    public string? ProjectName => Task.ProjectName;
    public string? AssignedBy => Task.AssignedBy;
    public string? Coordinator => Task.Coordinator;
    public string PlannedText => Task.PlannedStart.HasValue ? Labels.DateTime(Task.PlannedStart) : "";
    public string ExpectedResponseText => Task.ExpectedResponseAt.HasValue ? Labels.DateTime(Task.ExpectedResponseAt) : "";
    public string EstimateText => Task.EstimatedHours is > 0 ? $"{Task.EstimatedHours:0.#} giờ" : "Chưa ước tính";

    public string Explanation => Assessment?.Explanation ?? "";
    public string ScoreText => Assessment == null ? "" : $"{Assessment.Score:0}";
    public string SuggestionText => Assessment is { SuggestionDiffers: true }
        ? $"Đề xuất mức ưu tiên: {Labels.Of(Assessment.SuggestedPriority)}"
        : "";
    public string QuadrantText => Assessment == null ? "" : Labels.Of(Assessment.Quadrant);
    public bool TimeRisk => Assessment?.TimeRisk == true;

    /// <summary>Màu ưu tiên: Đỏ = khẩn cấp, Cam = cao, Xanh dương = bình thường, Xám = thấp.</summary>
    public string PriorityColor => Task.Priority switch
    {
        TaskPriority.Urgent => "Red",
        TaskPriority.High => "Orange",
        TaskPriority.Normal => "Blue",
        _ => "Gray"
    };

    /// <summary>Màu trạng thái: Xanh lá = hoàn thành, Xanh dương = đang thực hiện, Xám = chờ xử lý.</summary>
    public string StatusColor => Task.Status switch
    {
        WorkTaskStatus.Completed => "Green",
        WorkTaskStatus.InProgress => "Blue",
        WorkTaskStatus.NeedMoreDocuments => "Orange",
        WorkTaskStatus.Cancelled => "Gray",
        _ => "Gray"
    };

    /// <summary>Màu thời hạn: Đỏ = quá hạn, Cam = sắp đến hạn/hôm nay.</summary>
    public string DueColor
    {
        get
        {
            if (IsClosed) return Task.Status == WorkTaskStatus.Completed ? "Green" : "Gray";
            if (Task.DueAt < Now) return "Red";
            if ((Task.DueAt.Date - Now.Date).TotalDays <= 3) return "Orange";
            return "Gray";
        }
    }

    public IReadOnlyList<StatusOption> StatusOptions => StatusOption.All;

    public IAsyncRelayCommand StartCommand { get; }
    public IAsyncRelayCommand CompleteCommand { get; }
    public IAsyncRelayCommand<StatusOption> ChangeStatusCommand { get; }
    public IAsyncRelayCommand EditCommand { get; }
    public IRelayCommand OpenFolderCommand { get; }
}
