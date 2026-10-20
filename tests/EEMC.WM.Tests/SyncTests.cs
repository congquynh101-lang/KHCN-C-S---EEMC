using EEMC.WM.Core.Domain;
using EEMC.WM.Core.Time;
using EEMC.WM.Data;
using EEMC.WM.Data.Infrastructure;
using EEMC.WM.Data.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace EEMC.WM.Tests;

/// <summary>Mô phỏng nhiều máy tính dùng chung một thư mục đồng bộ (OneDrive).</summary>
public sealed class SyncTests : IAsyncLifetime
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "eemc-wm-sync", Guid.NewGuid().ToString("N"));
    private string SyncFolder => Path.Combine(_root, "OneDrive", "DongBo");

    private sealed class Machine
    {
        public required string Name { get; init; }
        public required TestDbFactory Factory { get; init; }
        public required FixedClock Clock { get; init; }
        public required SettingsService Settings { get; init; }
        public required TaskService Tasks { get; init; }
        public required SyncService Sync { get; init; }
        public required string DeviceId { get; init; }

        public async Task<List<WorkTask>> AllAsync()
        {
            await using var db = Factory.CreateDbContext();
            return await db.Tasks.AsNoTracking().OrderBy(t => t.Code).ToListAsync();
        }

        public async Task<WorkTask> ByTitleAsync(string title) => (await AllAsync()).Single(t => t.Title == title);
    }

    private readonly List<Machine> _machines = new();

    private async Task<Machine> NewMachineAsync(string name, DateTime now)
    {
        var dir = Path.Combine(_root, name);
        var paths = new AppPaths(dir);
        var factory = new TestDbFactory(paths.DatabasePath);
        var deviceId = Guid.NewGuid().ToString("N");
        AppDbContext.CurrentDeviceId = deviceId;
        await DbInitializer.InitializeAsync(factory);
        var settings = new SettingsService(factory);
        await settings.LoadAsync();
        var s = settings.CloneCurrent();
        s.SyncEnabled = true;
        s.SyncFolder = SyncFolder;
        await settings.SaveAsync(s);
        var clock = new FixedClock(now);
        var m = new Machine
        {
            Name = name, Factory = factory, Clock = clock, Settings = settings, DeviceId = deviceId,
            Tasks = new TaskService(factory, clock, settings),
            Sync = new SyncService(factory, settings, new DeviceIdentity(deviceId, name))
        };
        _machines.Add(m);
        return m;
    }

    /// <summary>Các thao tác trên máy dùng mã máy của máy đó (giống thực tế: mỗi tiến trình một mã).</summary>
    private static T On<T>(Machine m, Func<T> action)
    {
        AppDbContext.CurrentDeviceId = m.DeviceId;
        return action();
    }

    private static async Task<SyncResult> SyncAsync(Machine m)
    {
        AppDbContext.CurrentDeviceId = m.DeviceId;
        var r = await m.Sync.RunAsync();
        Assert.True(r.State == SyncState.Ok, r.Message);
        return r;
    }

    private static WorkTask New(Machine m, string title, int dueHours = 48) => new()
    {
        Title = title, ReceivedAt = m.Clock.Now, DueAt = m.Clock.Now.AddHours(dueHours), Priority = TaskPriority.Normal
    };

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_root, true); } catch { }
        return Task.CompletedTask;
    }

    private static readonly DateTime T0 = new(2026, 10, 19, 9, 0, 0);

    [Fact]
    public async Task Task_created_on_one_machine_appears_on_the_other_with_history()
    {
        var a = await NewMachineAsync("A", T0);
        var b = await NewMachineAsync("B", T0);
        var cats = await new CategoryService(a.Factory).GetAllAsync();
        var leaf = cats.First(c => c.ParentId != null);

        var t = New(a, "Báo cáo tiến độ kháng điện 500 kV");
        t.CategoryId = leaf.Id;
        var created = await On(a, () => a.Tasks.CreateAsync(t));
        await On(a, () => a.Tasks.AddCommentAsync(created.Id, "Đã gửi Phòng Kỹ thuật"));

        await SyncAsync(a);
        var rb = await SyncAsync(b);
        Assert.True(rb.Imported > 0);

        var onB = await b.ByTitleAsync("Báo cáo tiến độ kháng điện 500 kV");
        Assert.Equal(created.Code, onB.Code);
        Assert.Equal(created.SyncId, onB.SyncId);
        // Danh mục mặc định khớp nhau giữa hai máy nhờ định danh cố định
        var bCats = await new CategoryService(b.Factory).GetAllAsync();
        Assert.Equal(leaf.Name, bCats.Single(c => c.Id == onB.CategoryId).Name);
        var hist = await b.Tasks.GetHistoryAsync(onB.Id);
        Assert.Contains(hist, h => h.IsComment && h.Detail == "Đã gửi Phòng Kỹ thuật");
        Assert.Contains(hist, h => h.Action == "Tạo mới");
    }

    [Fact]
    public async Task Sync_is_idempotent_and_does_not_echo_imported_changes()
    {
        var a = await NewMachineAsync("A", T0);
        var b = await NewMachineAsync("B", T0);
        await On(a, () => a.Tasks.CreateAsync(New(a, "Một")));
        await SyncAsync(a);
        await SyncAsync(b);
        var again = await SyncAsync(b);
        Assert.Equal(0, again.Imported);
        Assert.Equal(0, again.Exported); // B không gửi lại dữ liệu vừa nhận
        var againA = await SyncAsync(a);
        Assert.Equal(0, againA.Imported);
    }

    [Fact]
    public async Task Edits_flow_both_ways_and_latest_edit_wins()
    {
        var a = await NewMachineAsync("A", T0);
        var b = await NewMachineAsync("B", T0);
        var t = await On(a, () => a.Tasks.CreateAsync(New(a, "Gốc")));
        await SyncAsync(a);
        await SyncAsync(b);

        // A sửa trước, B sửa sau (cùng công việc, chưa đồng bộ giữa chừng)
        var onA = (await a.Tasks.GetAsync(t.Id))!;
        onA.Title = "Sửa trên A";
        await On(a, () => a.Tasks.UpdateAsync(onA));
        await Task.Delay(5);
        var onB = await b.ByTitleAsync("Gốc");
        var full = (await b.Tasks.GetAsync(onB.Id))!;
        full.Title = "Sửa trên B";
        full.Priority = TaskPriority.Urgent;
        await On(b, () => b.Tasks.UpdateAsync(full));

        await SyncAsync(a);
        await SyncAsync(b);
        await SyncAsync(a);

        Assert.Equal("Sửa trên B", (await a.AllAsync()).Single().Title);
        Assert.Equal(TaskPriority.Urgent, (await a.AllAsync()).Single().Priority);
        Assert.Equal("Sửa trên B", (await b.AllAsync()).Single().Title);
    }

    [Fact]
    public async Task Status_change_and_trash_propagate()
    {
        var a = await NewMachineAsync("A", T0);
        var b = await NewMachineAsync("B", T0);
        var t = await On(a, () => a.Tasks.CreateAsync(New(a, "Trạng thái")));
        await SyncAsync(a);
        await SyncAsync(b);

        var onB = await b.ByTitleAsync("Trạng thái");
        await On(b, () => b.Tasks.ChangeStatusAsync(onB.Id, WorkTaskStatus.Completed));
        await SyncAsync(b);
        await SyncAsync(a);
        var onA = (await a.Tasks.GetAsync(t.Id))!;
        Assert.Equal(WorkTaskStatus.Completed, onA.Status);
        Assert.NotNull(onA.CompletedAt);

        await On(a, () => a.Tasks.MoveToTrashAsync(new[] { t.Id }));
        await SyncAsync(a);
        await SyncAsync(b);
        Assert.True((await b.ByTitleAsync("Trạng thái")).IsDeleted);
    }

    [Fact]
    public async Task Permanent_delete_propagates_and_is_not_resurrected()
    {
        var a = await NewMachineAsync("A", T0);
        var b = await NewMachineAsync("B", T0);
        var t = await On(a, () => a.Tasks.CreateAsync(New(a, "Sẽ xóa")));
        await SyncAsync(a);
        await SyncAsync(b);

        await On(a, () => a.Tasks.MoveToTrashAsync(new[] { t.Id }));
        await On(a, () => a.Tasks.PurgeAsync(new[] { t.Id }));
        await SyncAsync(a);
        await SyncAsync(b);
        Assert.Empty(await b.AllAsync());

        // Máy C tham gia sau, đọc toàn bộ lịch sử gói: không được "hồi sinh" công việc đã xóa
        var c = await NewMachineAsync("C", T0);
        await SyncAsync(c);
        Assert.Empty(await c.AllAsync());
    }

    [Fact]
    public async Task Offline_code_collision_is_resolved_by_renaming_the_later_task()
    {
        var a = await NewMachineAsync("A", T0);
        var b = await NewMachineAsync("B", T0.AddMinutes(30));
        var ta = await On(a, () => a.Tasks.CreateAsync(New(a, "Tạo trên A trước")));
        var tb = await On(b, () => b.Tasks.CreateAsync(New(b, "Tạo trên B sau")));
        Assert.Equal(ta.Code, tb.Code); // cùng CV-2026-00001 khi offline

        await SyncAsync(a);
        var rb = await SyncAsync(b);
        Assert.Equal(1, rb.CodesRenamed);
        await SyncAsync(a);

        var listA = await a.AllAsync();
        var listB = await b.AllAsync();
        Assert.Equal(2, listA.Select(x => x.Code).Distinct().Count());
        Assert.Equal(listA.Select(x => (x.SyncId, x.Code)).OrderBy(x => x.SyncId), listB.Select(x => (x.SyncId, x.Code)).OrderBy(x => x.SyncId));
        Assert.Equal(ta.Code, listA.Single(x => x.Title == "Tạo trên A trước").Code);
        Assert.Equal("CV-2026-00002", listA.Single(x => x.Title == "Tạo trên B sau").Code);

        // Mã mới tiếp theo trên A không trùng mã đã nhận từ B
        var next = await On(a, () => a.Tasks.CreateAsync(New(a, "Tiếp theo")));
        Assert.Equal("CV-2026-00003", next.Code);
    }

    [Fact]
    public async Task Shared_work_settings_sync_but_device_settings_do_not()
    {
        var a = await NewMachineAsync("A", T0);
        var b = await NewMachineAsync("B", T0);
        var s = a.Settings.CloneCurrent();
        s.WorkDays.Add(DayOfWeek.Saturday);
        s.DefaultEstimateHours = 3.5;
        s.DarkTheme = true;
        await a.Settings.SaveAsync(s);

        await SyncAsync(a);
        await SyncAsync(b);
        Assert.Contains(DayOfWeek.Saturday, b.Settings.Current.WorkDays);
        Assert.Equal(3.5, b.Settings.Current.DefaultEstimateHours);
        Assert.False(b.Settings.Current.DarkTheme);
        Assert.Equal(SyncFolder, b.Settings.Current.SyncFolder);
    }

    [Fact]
    public async Task Snooze_is_local_and_does_not_trigger_sync()
    {
        var a = await NewMachineAsync("A", T0);
        var t = await On(a, () => a.Tasks.CreateAsync(New(a, "Hoãn nhắc")));
        await SyncAsync(a);
        await On(a, () => a.Tasks.SnoozeAsync(t.Id, T0.AddHours(1)));
        var r = await SyncAsync(a);
        Assert.Equal(0, r.Exported);
    }

    [Fact]
    public async Task Custom_category_syncs()
    {
        var a = await NewMachineAsync("A", T0);
        var b = await NewMachineAsync("B", T0);
        var cs = new CategoryService(a.Factory);
        var group = await On(a, () => cs.AddAsync("Chuyển đổi số", null));
        var item = await On(a, () => cs.AddAsync("Số hóa hồ sơ", group.Id));
        var t = New(a, "Có nhóm mới");
        t.CategoryId = item.Id;
        await On(a, () => a.Tasks.CreateAsync(t));
        await SyncAsync(a);
        await SyncAsync(b);
        var bCats = await new CategoryService(b.Factory).GetAllAsync();
        var bItem = bCats.Single(c => c.Name == "Số hóa hồ sơ");
        Assert.Equal("Chuyển đổi số", bCats.Single(c => c.Id == bItem.ParentId).Name);
        Assert.Equal(bItem.Id, (await b.ByTitleAsync("Có nhóm mới")).CategoryId);
        Assert.Equal(9, bCats.Count(c => c.ParentId == null)); // 8 mặc định + 1, không bị nhân đôi
    }

    [Fact]
    public async Task Compaction_keeps_full_state_for_new_machines()
    {
        var a = await NewMachineAsync("A", T0);
        a.Sync.CompactThreshold = 4;
        for (int i = 0; i < 6; i++)
        {
            await On(a, () => a.Tasks.CreateAsync(New(a, $"Việc {i}")));
            await SyncAsync(a);
        }
        var myDir = Path.Combine(SyncFolder, "devices", a.DeviceId);
        Assert.True(Directory.GetFiles(myDir, "*.json").Length <= 5);
        Assert.Contains(Directory.GetFiles(myDir), f => f.EndsWith("-s.json"));

        var c = await NewMachineAsync("C", T0);
        await SyncAsync(c);
        Assert.Equal(6, (await c.AllAsync()).Count);
    }

    [Fact]
    public async Task Existing_data_before_enabling_sync_is_sent_on_first_sync()
    {
        var a = await NewMachineAsync("A", T0);
        var s = a.Settings.CloneCurrent();
        s.SyncEnabled = false;
        await a.Settings.SaveAsync(s);
        await On(a, () => a.Tasks.CreateAsync(New(a, "Có từ trước")));
        Assert.Equal(SyncState.Disabled, (await a.Sync.RunAsync()).State);

        s.SyncEnabled = true;
        await a.Settings.SaveAsync(s);
        await SyncAsync(a);
        var b = await NewMachineAsync("B", T0);
        await SyncAsync(b);
        Assert.Single(await b.AllAsync());
    }

    [Fact]
    public async Task Unreachable_folder_reports_error_without_throwing()
    {
        var a = await NewMachineAsync("A", T0);
        var s = a.Settings.CloneCurrent();
        s.SyncFolder = "Z:\\khong-ton-tai\\" + new string('x', 5) + "\\<>|";
        await a.Settings.SaveAsync(s);
        var r = await a.Sync.RunAsync();
        Assert.Equal(SyncState.Error, r.State);
        Assert.Contains("đồng bộ", r.Message);
    }
}
