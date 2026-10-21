using EEMC.WM.Core.Domain;
using EEMC.WM.Data.Infrastructure;
using EEMC.WM.Data.Services;
using Microsoft.JSInterop;

namespace EEMC.WM.Web.Services;

/// <summary>Phát thông báo nhắc việc mới tới mọi trình duyệt đang mở (máy tính, điện thoại...).</summary>
public sealed class NotificationBus
{
    public event Action<IReadOnlyList<AppNotification>>? Published;
    public void Publish(IReadOnlyList<AppNotification> items) => Published?.Invoke(items);
}

/// <summary>Kiểm tra nhắc việc mỗi phút trên máy chủ (không phụ thuộc trình duyệt có mở hay không).</summary>
public sealed class ReminderWorker : BackgroundService
{
    private readonly ReminderService _reminders;
    private readonly NotificationService _notifications;
    private readonly NotificationBus _bus;

    public ReminderWorker(ReminderService reminders, NotificationService notifications, NotificationBus bus)
    {
        _reminders = reminders;
        _notifications = notifications;
        _bus = bus;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(60));
        do
        {
            try
            {
                var created = await _reminders.CheckAsync();
                if (created.Count > 0)
                {
                    _notifications.RaiseChanged();
                    _bus.Publish(created);
                }
            }
            catch (Exception ex) { AppLog.Error(ex, "Kiểm tra nhắc việc (web)"); }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}

/// <summary>Sao lưu tự động mỗi ngày và dọn thông báo cũ.</summary>
public sealed class BackupWorker : BackgroundService
{
    private readonly BackupService _backup;
    private readonly NotificationService _notifications;

    public BackupWorker(BackupService backup, NotificationService notifications)
    {
        _backup = backup;
        _notifications = notifications;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(1));
        do
        {
            try
            {
                await _backup.AutoBackupIfNeededAsync();
                await _notifications.CleanupAsync();
            }
            catch (Exception ex) { AppLog.Error(ex, "Sao lưu tự động (web)"); }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}

public enum ToastKind { Info, Success, Warning, Error }

public sealed record Toast(Guid Id, string Message, ToastKind Kind);

/// <summary>Yêu cầu mở cửa sổ thêm/sửa công việc. Title/Notes điền sẵn cho công việc mới; OnCreated gọi sau khi tạo xong.</summary>
public sealed record EditorRequest(int? TaskId, DateTime? DueDate = null, int? ProjectId = null, int? ParentId = null,
    string? Title = null, string? Notes = null, Func<WorkTask, Task>? OnCreated = null);

/// <summary>Thông báo nhanh, hộp xác nhận và tải tệp cho từng phiên trình duyệt.</summary>
public sealed class UiService
{
    private readonly IJSRuntime _js;
    public UiService(IJSRuntime js) { _js = js; }

    public List<Toast> Toasts { get; } = new();
    public event Action? Changed;

    public event Action<EditorRequest>? EditorRequested;
    public void RequestEditor(EditorRequest r) => EditorRequested?.Invoke(r);

    public string? ConfirmMessage { get; private set; }
    public string ConfirmOk { get; private set; } = "Đồng ý";
    public bool ConfirmDanger { get; private set; }
    private TaskCompletionSource<bool>? _confirm;

    public void Show(string message, ToastKind kind = ToastKind.Info)
    {
        var t = new Toast(Guid.NewGuid(), message, kind);
        Toasts.Add(t);
        Changed?.Invoke();
        _ = Task.Delay(kind == ToastKind.Error ? 8000 : 4000).ContinueWith(_ => Dismiss(t.Id));
    }

    public void Success(string m) => Show(m, ToastKind.Success);
    public void Error(string m) => Show(m, ToastKind.Error);

    public void Error(Exception ex, string context)
    {
        if (ex is ValidationException v) { Show(string.Join(" ", v.Errors), ToastKind.Warning); return; }
        if (ex is InvalidOperationException io) { Show(io.Message, ToastKind.Error); return; }
        AppLog.Error(ex, context);
        Show($"{context}. Chi tiết đã được ghi vào nhật ký lỗi.", ToastKind.Error);
    }

    public void Dismiss(Guid id)
    {
        if (Toasts.RemoveAll(t => t.Id == id) > 0) Changed?.Invoke();
    }

    public Task<bool> ConfirmAsync(string message, string ok = "Đồng ý", bool danger = false)
    {
        _confirm?.TrySetResult(false);
        _confirm = new TaskCompletionSource<bool>();
        ConfirmMessage = message;
        ConfirmOk = ok;
        ConfirmDanger = danger;
        Changed?.Invoke();
        return _confirm.Task;
    }

    public void ResolveConfirm(bool value)
    {
        ConfirmMessage = null;
        Changed?.Invoke();
        _confirm?.TrySetResult(value);
    }

    /// <summary>Tải tệp do máy chủ tạo (báo cáo, Excel) về trình duyệt.</summary>
    public async Task DownloadAsync(byte[] bytes, string fileName)
    {
        using var stream = new MemoryStream(bytes);
        using var reference = new DotNetStreamReference(stream);
        await _js.InvokeVoidAsync("eemc.download", fileName, reference);
    }

    /// <summary>Hiện thông báo của hệ điều hành (Windows/Android) qua trình duyệt nếu được cho phép.</summary>
    public ValueTask BrowserNotifyAsync(string title, string body) => _js.InvokeVoidAsync("eemc.notify", title, body);
}
