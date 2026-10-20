using EEMC.WM.Core.Domain;
using EEMC.WM.Core.Time;
using EEMC.WM.Data;
using EEMC.WM.Data.Infrastructure;
using EEMC.WM.Data.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace EEMC.WM.Tests;

internal sealed class TestDbFactory : IDbContextFactory<AppDbContext>
{
    private readonly DbContextOptions<AppDbContext> _options;
    public TestDbFactory(string path) =>
        _options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite($"Data Source={path}").Options;
    public AppDbContext CreateDbContext() => new(_options);
}

public sealed class DataServiceTests : IAsyncLifetime
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "eemc-wm-tests", Guid.NewGuid().ToString("N"));
    private readonly FixedClock _clock = new(new DateTime(2026, 10, 19, 9, 0, 0));
    private TestDbFactory _factory = null!;
    private SettingsService _settings = null!;
    private TaskService _tasks = null!;
    private AppPaths _paths = null!;

    public async Task InitializeAsync()
    {
        _paths = new AppPaths(_dir);
        _factory = new TestDbFactory(_paths.DatabasePath);
        await DbInitializer.InitializeAsync(_factory);
        _settings = new SettingsService(_factory);
        await _settings.LoadAsync();
        _tasks = new TaskService(_factory, _clock, _settings);
    }

    public Task DisposeAsync()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, true); } catch { }
        return Task.CompletedTask;
    }

    private WorkTask New(string title, int dueHours = 48, TaskPriority p = TaskPriority.Normal) => new()
    {
        Title = title, ReceivedAt = _clock.Now, DueAt = _clock.Now.AddHours(dueHours), Priority = p
    };

    [Fact]
    public async Task Catalog_is_seeded_without_any_task_data()
    {
        var cats = await new CategoryService(_factory).GetAllAsync();
        Assert.Equal(8, cats.Count(c => c.ParentId == null));
        await using var db = _factory.CreateDbContext();
        Assert.Equal(0, await db.Tasks.CountAsync());
    }

    [Fact]
    public async Task Create_generates_unique_sequential_codes_and_history()
    {
        var a = await _tasks.CreateAsync(New("Một"));
        var b = await _tasks.CreateAsync(New("Hai"));
        Assert.Equal("CV-2026-00001", a.Code);
        Assert.Equal("CV-2026-00002", b.Code);
        var h = await _tasks.GetHistoryAsync(a.Id);
        Assert.Contains(h, x => x.Action == "Tạo mới");
    }

    [Fact]
    public async Task Only_four_fields_are_required()
    {
        var t = await _tasks.CreateAsync(New("Chỉ 4 trường"));
        Assert.True(t.Id > 0);
        await Assert.ThrowsAsync<ValidationException>(() => _tasks.CreateAsync(new WorkTask { ReceivedAt = _clock.Now, DueAt = _clock.Now }));
        var bad = New("Hạn trước ngày nhận");
        bad.DueAt = bad.ReceivedAt.AddHours(-1);
        await Assert.ThrowsAsync<ValidationException>(() => _tasks.CreateAsync(bad));
    }

    [Fact]
    public async Task Completion_only_through_explicit_status_change()
    {
        var t = await _tasks.CreateAsync(New("Tiến độ 100% chưa phải hoàn thành"));
        t.Progress = 100;
        await _tasks.UpdateAsync(t);
        var reloaded = await _tasks.GetAsync(t.Id);
        Assert.Equal(WorkTaskStatus.NotStarted, reloaded!.Status);
        Assert.Null(reloaded.CompletedAt);

        await _tasks.ChangeStatusAsync(t.Id, WorkTaskStatus.Completed);
        reloaded = await _tasks.GetAsync(t.Id);
        Assert.Equal(WorkTaskStatus.Completed, reloaded!.Status);
        Assert.Equal(_clock.Now, reloaded.CompletedAt);
        Assert.Equal(_clock.Now, reloaded.ClosedAt);

        await _tasks.ChangeStatusAsync(t.Id, WorkTaskStatus.InProgress);
        reloaded = await _tasks.GetAsync(t.Id);
        Assert.Null(reloaded!.CompletedAt);
    }

    [Fact]
    public async Task Update_records_field_changes()
    {
        var t = await _tasks.CreateAsync(New("Ban đầu"));
        t.Title = "Đã sửa";
        t.Priority = TaskPriority.Urgent;
        await _tasks.UpdateAsync(t);
        var h = await _tasks.GetHistoryAsync(t.Id);
        var upd = h.First(x => x.Action.StartsWith("Cập nhật"));
        Assert.Contains("Nội dung", upd.Detail);
        Assert.Contains("Khẩn cấp", upd.Detail);
    }

    [Fact]
    public async Task Trash_restore_and_purge()
    {
        var t = await _tasks.CreateAsync(New("Xóa thử"));
        await _tasks.MoveToTrashAsync(new[] { t.Id });
        Assert.Equal(0, (await _tasks.QueryAsync(new TaskQuery())).Total);
        Assert.Equal(1, (await _tasks.QueryAsync(new TaskQuery { Deleted = true })).Total);
        await _tasks.RestoreAsync(new[] { t.Id });
        Assert.Equal(1, (await _tasks.QueryAsync(new TaskQuery())).Total);
        await _tasks.MoveToTrashAsync(new[] { t.Id });
        await _tasks.PurgeAsync(new[] { t.Id });
        Assert.Null(await _tasks.GetAsync(t.Id));
    }

    [Fact]
    public async Task Search_ignores_diacritics_and_filters_work()
    {
        await _tasks.CreateAsync(New("Báo cáo tiến độ đề tài kháng điện 500 kV", 4, TaskPriority.High));
        await _tasks.CreateAsync(New("Soạn tờ trình sáng kiến", 200));
        var r = await _tasks.QueryAsync(new TaskQuery { Search = "khang dien" });
        Assert.Equal(1, r.Total);
        Assert.Equal(1, (await _tasks.QueryAsync(new TaskQuery { Time = TimeFilter.Today })).Total);
        Assert.Equal(1, (await _tasks.QueryAsync(new TaskQuery { Priority = TaskPriority.High })).Total);
        var sugg = await _tasks.QueryAsync(new TaskQuery { Sort = SortField.Suggested });
        Assert.StartsWith("Báo cáo", sugg.Items[0].Title);
    }

    [Fact]
    public async Task Paging_handles_10000_tasks()
    {
        await using (var db = _factory.CreateDbContext())
        {
            for (int i = 0; i < 10_000; i++)
            {
                var t = New($"Công việc số {i}", i % 500);
                t.Code = $"CV-2026-{i + 1:00000}";
                t.CreatedAt = t.UpdatedAt = _clock.Now;
                t.RefreshSearchText();
                db.Tasks.Add(t);
            }
            await db.SaveChangesAsync();
        }
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var page = await _tasks.QueryAsync(new TaskQuery { Page = 3, PageSize = 100 });
        var suggested = await _tasks.QueryAsync(new TaskQuery { Sort = SortField.Suggested, PageSize = 100 });
        var stats = await new DashboardService(_factory, _clock, _settings).GetStatsAsync();
        sw.Stop();
        Assert.Equal(10_000, page.Total);
        Assert.Equal(100, page.Items.Count);
        Assert.Equal(100, page.PageCount);
        Assert.Equal(100, suggested.Items.Count);
        Assert.Equal(10_000, stats.Open);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(10), $"Quá chậm: {sw.Elapsed}");
        // Mã tiếp theo không trùng dữ liệu đã có
        var next = await _tasks.CreateAsync(New("Mới"));
        Assert.Equal("CV-2026-10001", next.Code);
    }

    [Fact]
    public async Task Reminder_service_does_not_duplicate_notifications()
    {
        var t = New("Sắp đến hạn", 2);
        t.ReceivedAt = _clock.Now.AddDays(-5);
        var created = await _tasks.CreateAsync(t);
        await using (var db = _factory.CreateDbContext())
        {
            // Giả lập công việc được tạo từ trước để các mốc nhắc có hiệu lực
            await db.Tasks.Where(x => x.Id == created.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.CreatedAt, _clock.Now.AddDays(-5)));
        }
        var reminders = new ReminderService(_factory, _clock, _settings);
        var first = await reminders.CheckAsync();
        var second = await reminders.CheckAsync();
        Assert.Single(first);
        Assert.Empty(second);
        var notifications = new NotificationService(_factory, _clock);
        Assert.Equal(1, await notifications.CountUnreadAsync());

        // Đổi hạn → mốc nhắc được đặt lại
        var reloaded = (await _tasks.GetAsync(created.Id))!;
        reloaded.DueAt = _clock.Now.AddMinutes(30);
        await _tasks.UpdateAsync(reloaded);
        Assert.Single(await reminders.CheckAsync());
    }

    [Fact]
    public async Task Backup_and_restore_roundtrip()
    {
        await _settings.SaveAsync(new Core.Settings.WorkSettings { BackupFolder = Path.Combine(_dir, "bk") });
        var backup = new BackupService(_paths, _settings);
        await _tasks.CreateAsync(New("Trước sao lưu"));
        var info = await backup.BackupNowAsync();
        Assert.True(BackupService.IsValidBackup(info.Path, out _));

        await _tasks.CreateAsync(New("Sau sao lưu"));
        Assert.Equal(2, (await _tasks.QueryAsync(new TaskQuery())).Total);

        var safety = await backup.RestoreAsync(info.Path);
        Assert.True(File.Exists(safety.Path));
        Assert.Equal(1, (await _tasks.QueryAsync(new TaskQuery())).Total);

        var junk = Path.Combine(_dir, "junk.db");
        await File.WriteAllTextAsync(junk, "not a db");
        Assert.False(BackupService.IsValidBackup(junk, out _));
    }

    [Fact]
    public async Task Settings_roundtrip_and_validation()
    {
        var s = _settings.CloneCurrent();
        s.WorkDays.Add(DayOfWeek.Saturday);
        s.DefaultEstimateHours = 3;
        await _settings.SaveAsync(s);
        var other = new SettingsService(_factory);
        await other.LoadAsync();
        Assert.Contains(DayOfWeek.Saturday, other.Current.WorkDays);
        Assert.Equal(2, other.Current.WorkPeriods.Count);
        Assert.Equal(3, other.Current.DefaultEstimateHours);

        var bad = _settings.CloneCurrent();
        bad.WorkPeriods.Clear();
        await Assert.ThrowsAsync<ValidationException>(() => _settings.SaveAsync(bad));
    }

    [Fact]
    public async Task Excel_export_writes_file()
    {
        await _tasks.CreateAsync(New("Xuất Excel"));
        var path = Path.Combine(_dir, "export.xlsx");
        var all = await _tasks.QueryAllAsync(new TaskQuery());
        await new ExcelExportService().ExportTasksAsync(all, path, _clock.Now, "Danh sách công việc");
        Assert.True(new FileInfo(path).Length > 1000);
    }
}
