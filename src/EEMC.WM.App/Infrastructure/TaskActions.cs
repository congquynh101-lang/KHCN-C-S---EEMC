using System.Windows;
using EEMC.WM.App.ViewModels;
using EEMC.WM.App.Views;
using EEMC.WM.Core.Domain;
using EEMC.WM.Core.Text;
using EEMC.WM.Core.Time;
using EEMC.WM.Data.Services;
using Microsoft.Extensions.DependencyInjection;

namespace EEMC.WM.App.Infrastructure;

/// <summary>Các thao tác trên công việc dùng chung cho mọi màn hình.</summary>
public sealed class TaskActions
{
    private readonly IServiceProvider _sp;
    private readonly TaskService _tasks;
    private readonly DialogService _dialogs;
    private readonly IClock _clock;
    private readonly SettingsService _settings;

    public TaskActions(IServiceProvider sp, TaskService tasks, DialogService dialogs, IClock clock, SettingsService settings)
    {
        _sp = sp;
        _tasks = tasks;
        _dialogs = dialogs;
        _clock = clock;
        _settings = settings;
    }

    private static Window? ActiveWindow =>
        Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive) ?? Application.Current.MainWindow;

    /// <summary>Mở cửa sổ nhập nhanh công việc mới.</summary>
    public async Task<bool> AddNewAsync(DateTime? dueDate = null)
    {
        var vm = _sp.GetRequiredService<TaskEditorViewModel>();
        await vm.InitializeNewAsync(dueDate);
        return ShowEditor(vm);
    }

    public async Task<bool> EditAsync(int id)
    {
        var vm = _sp.GetRequiredService<TaskEditorViewModel>();
        if (!await vm.InitializeEditAsync(id))
        {
            _dialogs.Warn("Không tìm thấy công việc (có thể đã bị xóa).");
            return false;
        }
        return ShowEditor(vm);
    }

    private static bool ShowEditor(TaskEditorViewModel vm)
    {
        var w = new TaskEditorWindow { DataContext = vm };
        var owner = ActiveWindow;
        if (owner != null && owner != w && owner.IsVisible) w.Owner = owner;
        return w.ShowDialog() == true;
    }

    public async Task StartAsync(TaskItemViewModel item)
    {
        try { await _tasks.ChangeStatusAsync(item.Id, WorkTaskStatus.InProgress); }
        catch (Exception ex) { _dialogs.Error("Không đổi được trạng thái.", ex); }
    }

    /// <summary>Hoàn thành chỉ được ghi nhận khi người dùng xác nhận.</summary>
    public async Task CompleteAsync(TaskItemViewModel item) => await ChangeStatusAsync(item, WorkTaskStatus.Completed);

    public async Task ChangeStatusAsync(TaskItemViewModel item, WorkTaskStatus status)
    {
        if (item.Task.Status == status) return;
        if (status == WorkTaskStatus.Completed &&
            !_dialogs.Confirm($"Xác nhận HOÀN THÀNH công việc?\n\n[{item.Code}] {item.Title}\nHạn: {item.DueText}"))
            return;
        if (status == WorkTaskStatus.Cancelled &&
            !_dialogs.Confirm($"Xác nhận HỦY công việc?\n\n[{item.Code}] {item.Title}"))
            return;
        try { await _tasks.ChangeStatusAsync(item.Id, status); }
        catch (Exception ex) { _dialogs.Error("Không đổi được trạng thái.", ex); }
    }

    public async Task<bool> MoveToTrashAsync(IReadOnlyCollection<TaskItemViewModel> items)
    {
        if (items.Count == 0) return false;
        var msg = items.Count == 1
            ? $"Chuyển công việc sau vào thùng rác?\n\n[{items.First().Code}] {items.First().Title}\n\n(Có thể khôi phục từ Thùng rác.)"
            : $"Chuyển {items.Count} công việc đã chọn vào thùng rác?\n\n(Có thể khôi phục từ Thùng rác.)";
        if (!_dialogs.Confirm(msg)) return false;
        try
        {
            await _tasks.MoveToTrashAsync(items.Select(i => i.Id).ToList());
            return true;
        }
        catch (Exception ex) { _dialogs.Error("Không xóa được công việc.", ex); return false; }
    }

    public async Task DuplicateAsync(TaskItemViewModel item)
    {
        try
        {
            var copy = await _tasks.DuplicateAsync(item.Id);
            if (_dialogs.Confirm($"Đã tạo bản sao {copy.Code}. Mở để chỉnh sửa ngay?"))
                await EditAsync(copy.Id);
        }
        catch (Exception ex) { _dialogs.Error("Không sao chép được công việc.", ex); }
    }

    /// <summary>Tạm hoãn nhắc việc: 15 phút, 1 giờ hoặc đến đầu giờ làm việc ngày hôm sau.</summary>
    public async Task SnoozeAsync(int taskId, string mode)
    {
        var now = _clock.Now;
        DateTime until = mode switch
        {
            "15" => now.AddMinutes(15),
            "60" => now.AddHours(1),
            _ => NextWorkDayStart(now)
        };
        try { await _tasks.SnoozeAsync(taskId, until); }
        catch (Exception ex) { _dialogs.Error("Không tạm hoãn được nhắc việc.", ex); }
    }

    private DateTime NextWorkDayStart(DateTime now)
    {
        var s = _settings.Current;
        var start = s.WorkPeriods.Count > 0 ? s.WorkPeriods.Min(p => p.Start) : TimeSpan.FromHours(8);
        var d = now.Date.AddDays(1);
        for (int i = 0; i < 14 && !s.IsWorkDay(d); i++) d = d.AddDays(1);
        return d + start;
    }

    public static string SnoozeLabel(string mode) => mode switch
    {
        "15" => "15 phút",
        "60" => "1 giờ",
        _ => "ngày làm việc tiếp theo"
    };

    public void OpenFolder(string? path) => _dialogs.OpenPath(path);

    public string Describe(WorkTask t) => $"[{t.Code}] {t.Title} — hạn {Labels.DateTime(t.DueAt)}";
}
