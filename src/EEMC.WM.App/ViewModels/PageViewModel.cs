using CommunityToolkit.Mvvm.ComponentModel;
using EEMC.WM.Data.Services;

namespace EEMC.WM.App.ViewModels;

public enum PageKey
{
    Dashboard,
    Today,
    Tasks,
    Calendar,
    Projects,
    Documents,
    Reports,
    Settings
}

public abstract partial class PageViewModel : ObservableObject
{
    [ObservableProperty] private bool _isLoading;

    public abstract string Title { get; }

    /// <summary>Tải lại dữ liệu của màn hình.</summary>
    public abstract Task RefreshAsync();
}

/// <summary>Yêu cầu mở danh sách công việc với bộ lọc định sẵn (từ Dashboard, ô tìm kiếm...).</summary>
public sealed class TaskListRequest
{
    public string? Search { get; init; }
    public StatusFilter Status { get; init; } = StatusFilter.Open;
    public Core.Domain.WorkTaskStatus? SpecificStatus { get; init; }
    public TimeFilter Time { get; init; } = TimeFilter.All;
    public Core.Domain.TaskPriority? Priority { get; init; }
    public bool HighOrUrgent { get; init; }
}

/// <summary>Điều hướng giữa các màn hình (tránh phụ thuộc vòng giữa các ViewModel).</summary>
public sealed class Navigator
{
    public event Action<PageKey>? NavigateRequested;
    public event Action<TaskListRequest>? TaskListRequested;

    public void Go(PageKey key) => NavigateRequested?.Invoke(key);
    public void ShowTasks(TaskListRequest request) => TaskListRequested?.Invoke(request);
}
