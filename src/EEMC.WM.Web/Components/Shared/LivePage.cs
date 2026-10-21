using EEMC.WM.Core.Domain;
using EEMC.WM.Core.Text;
using EEMC.WM.Core.Time;
using EEMC.WM.Data.Services;
using EEMC.WM.Web.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;

namespace EEMC.WM.Web.Components.Shared;

/// <summary>
/// Trang tự làm mới khi dữ liệu thay đổi (kể cả thay đổi từ trình duyệt/thiết bị khác đang mở cùng lúc).
/// </summary>
public abstract class LivePage : ComponentBase, IDisposable
{
    [Inject] protected TaskService Tasks { get; set; } = default!;
    [Inject] protected SettingsService Settings { get; set; } = default!;
    [Inject] protected IClock Clock { get; set; } = default!;
    [Inject] protected UiService UiSvc { get; set; } = default!;
    [Inject] protected NavigationManager Nav { get; set; } = default!;

    [CascadingParameter] private Task<AuthenticationState>? PageAuthState { get; set; }

    protected DateTime Now => Clock.Now;
    protected bool Loading { get; set; } = true;
    /// <summary>Vai trò để ẩn/hiện nút trên giao diện; quyền thật được kiểm tra trong dịch vụ dữ liệu.</summary>
    protected UserRole Role { get; private set; } = UserRole.Viewer;
    protected bool CanEdit => Role >= UserRole.Editor;
    protected bool IsAdmin => Role == UserRole.Admin;
    /// <summary>Tên hiển thị của người đang đăng nhập.</summary>
    protected string UserName { get; private set; } = "";
    private CancellationTokenSource? _debounce;

    protected override async Task OnInitializedAsync()
    {
        if (PageAuthState != null)
        {
            var user = (await PageAuthState).User;
            Role = AccessWiring.RoleOf(user);
            UserName = user.Identity?.Name ?? "";
        }
        Tasks.TasksChanged += OnDataChanged;
        Settings.Changed += OnDataChanged;
        await SafeLoadAsync();
    }

    protected abstract Task LoadAsync();

    protected async Task SafeLoadAsync()
    {
        try
        {
            Loading = true;
            await LoadAsync();
        }
        catch (Exception ex) { UiSvc.Error(ex, "Không tải được dữ liệu"); }
        finally { Loading = false; }
    }

    private void OnDataChanged(object? sender, EventArgs e)
    {
        _debounce?.Cancel();
        var cts = _debounce = new CancellationTokenSource();
        _ = Task.Delay(250, cts.Token).ContinueWith(async t =>
        {
            if (t.IsCanceled) return;
            await InvokeAsync(async () =>
            {
                await SafeLoadAsync();
                StateHasChanged();
            });
        }, TaskScheduler.Default);
    }

    // ---------------- Thao tác dùng chung

    protected void OpenEditor(int? id = null, DateTime? due = null, int? projectId = null, int? parentId = null) =>
        UiSvc.RequestEditor(new EditorRequest(id, due, projectId, parentId));

    protected async Task SetStatusAsync(WorkTask t, WorkTaskStatus s)
    {
        if (t.Status == s) return;
        if (s == WorkTaskStatus.Completed && !await UiSvc.ConfirmAsync($"Xác nhận HOÀN THÀNH công việc?\n[{t.Code}] {t.Title}", "Hoàn thành")) return;
        if (s == WorkTaskStatus.Cancelled && !await UiSvc.ConfirmAsync($"Xác nhận HỦY công việc?\n[{t.Code}] {t.Title}", "Hủy công việc", danger: true)) return;
        try
        {
            await Tasks.ChangeStatusAsync(t.Id, s);
            UiSvc.Success($"{t.Code}: {Labels.Of(s)}");
        }
        catch (Exception ex) { UiSvc.Error(ex, "Không đổi được trạng thái"); }
    }

    public virtual void Dispose()
    {
        Tasks.TasksChanged -= OnDataChanged;
        Settings.Changed -= OnDataChanged;
        _debounce?.Cancel();
        GC.SuppressFinalize(this);
    }
}
