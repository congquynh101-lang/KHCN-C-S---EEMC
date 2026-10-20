using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EEMC.WM.App.Infrastructure;
using EEMC.WM.Core.Domain;
using EEMC.WM.Core.Text;
using EEMC.WM.Core.Time;
using EEMC.WM.Data.Services;

namespace EEMC.WM.App.ViewModels;

public sealed class DocumentRow
{
    public required TaskItemViewModel Item { get; init; }
    public string DocumentStatusText => Labels.Of(Item.Task.DocumentStatus);
    public string? DocumentNumber => Item.Task.DocumentNumber;
    public string? FolderPath => Item.Task.FolderPath;
}

/// <summary>Hồ sơ tài liệu gắn với công việc: tìm theo tên công việc, dự án, số văn bản, từ khóa; mở bằng ứng dụng mặc định.</summary>
public sealed partial class DocumentsViewModel : PageViewModel
{
    private readonly TaskService _tasks;
    private readonly TaskActions _actions;
    private readonly DialogService _dialogs;
    private readonly IClock _clock;

    public DocumentsViewModel(TaskService tasks, TaskActions actions, DialogService dialogs, IClock clock)
    {
        _tasks = tasks;
        _actions = actions;
        _dialogs = dialogs;
        _clock = clock;
        DocumentChoices = new List<Choice<DocumentStatus?>> { new("Mọi tình trạng hồ sơ", null) };
        DocumentChoices.AddRange(Enum.GetValues<DocumentStatus>().Select(d => new Choice<DocumentStatus?>(Labels.Of(d), d)));
        _selectedDocument = DocumentChoices[0];
    }

    public override string Title => "Hồ sơ tài liệu";

    public List<Choice<DocumentStatus?>> DocumentChoices { get; }
    public ObservableCollection<DocumentRow> Rows { get; } = new();

    [ObservableProperty] private string _search = "";
    [ObservableProperty] private Choice<DocumentStatus?> _selectedDocument;
    [ObservableProperty] private bool _includeClosed = true;
    [ObservableProperty] private string _summary = "";

    partial void OnSelectedDocumentChanged(Choice<DocumentStatus?> value) => _ = RefreshAsync();
    partial void OnIncludeClosedChanged(bool value) => _ = RefreshAsync();

    [RelayCommand] private Task SearchNow() => RefreshAsync();

    public override async Task RefreshAsync()
    {
        IsLoading = true;
        try
        {
            var now = _clock.Now;
            var r = await _tasks.QueryAsync(new TaskQuery
            {
                Search = Search,
                OnlyWithDocuments = true,
                DocumentStatus = SelectedDocument.Value,
                StatusFilter = IncludeClosed ? StatusFilter.All : StatusFilter.Open,
                Sort = SortField.UpdatedAt,
                SortDescending = true,
                PageSize = 500
            });
            Rows.Clear();
            foreach (var t in r.Items) Rows.Add(new DocumentRow { Item = new TaskItemViewModel(t, now, _actions) });
            Summary = r.Total == 0
                ? "Chưa có công việc nào gắn hồ sơ. Nhập đường dẫn thư mục/tệp, số văn bản hoặc tình trạng hồ sơ trong \"Thông tin mở rộng\" của công việc."
                : r.Total > r.Items.Count ? $"Hiển thị {r.Items.Count} / {r.Total} kết quả mới cập nhật nhất — hãy thu hẹp từ khóa." : $"{r.Total} công việc có hồ sơ";
        }
        catch (Exception ex) { _dialogs.Error("Không tải được danh sách hồ sơ.", ex); }
        finally { IsLoading = false; }
    }

    [RelayCommand]
    private void Open(DocumentRow? row)
    {
        if (row?.FolderPath is { Length: > 0 } p) _dialogs.OpenPath(p);
        else _dialogs.Info("Công việc này chưa có đường dẫn hồ sơ.");
    }
}

/// <summary>Màn hình của tính năng thuộc giai đoạn sau (hiển thị rõ ràng, không có nút giả).</summary>
public sealed class PlannedFeatureViewModel : PageViewModel
{
    public PlannedFeatureViewModel(string title, string description, IReadOnlyList<string> features)
    {
        _title = title;
        Description = description;
        Features = features;
    }

    private readonly string _title;
    public override string Title => _title;
    public string Description { get; }
    public IReadOnlyList<string> Features { get; }
    public override Task RefreshAsync() => Task.CompletedTask;
}
