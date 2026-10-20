using System.Text;
using EEMC.WM.Core.Domain;
using EEMC.WM.Core.Services;
using EEMC.WM.Core.Settings;
using EEMC.WM.Core.Time;
using EEMC.WM.Data;
using EEMC.WM.Data.Infrastructure;
using EEMC.WM.Data.Reports;
using EEMC.WM.Data.Services;
using Microsoft.Data.Sqlite;

namespace EEMC.WM.Tests;

public sealed class Phase2Tests : IAsyncLifetime
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "eemc-wm-p2", Guid.NewGuid().ToString("N"));
    private readonly FixedClock _clock = new(new DateTime(2026, 10, 19, 9, 0, 0)); // Thứ Hai
    private AppPaths _paths = null!;
    private TestDbFactory _factory = null!;
    private SettingsService _settings = null!;
    private TaskService _tasks = null!;

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

    private WorkTask New(string title, int dueHours = 48, double? est = null) => new()
    {
        Title = title, ReceivedAt = _clock.Now, DueAt = _clock.Now.AddHours(dueHours), Priority = TaskPriority.Normal, EstimatedHours = est
    };

    // ---------------------------------------------------------------- Công việc con

    [Fact]
    public void Weighted_progress_counts_completed_as_100_and_skips_cancelled()
    {
        var children = new[]
        {
            new WorkTask { Weight = 1, Progress = 0, Status = WorkTaskStatus.Completed },
            new WorkTask { Weight = 3, Progress = 50, Status = WorkTaskStatus.InProgress },
            new WorkTask { Weight = 5, Progress = 0, Status = WorkTaskStatus.Cancelled },
        };
        Assert.Equal(63, ProgressCalculator.Weighted(children)); // (100*1 + 50*3)/4 = 62.5 → 63
        Assert.Null(ProgressCalculator.Weighted(Array.Empty<WorkTask>()));
    }

    [Fact]
    public async Task Parent_progress_updates_from_subtasks_but_status_is_not_auto_completed()
    {
        var parent = await _tasks.CreateAsync(New("Hoàn thiện hồ sơ doanh nghiệp KH&CN", 24 * 30));
        var a = await _tasks.CreateSubtaskAsync(parent.Id, New("Rà soát căn cứ pháp lý"));
        var b = await _tasks.CreateSubtaskAsync(parent.Id, New("Soạn tờ trình"));
        await _tasks.ChangeStatusAsync(a.Id, WorkTaskStatus.Completed);
        Assert.Equal(50, (await _tasks.GetAsync(parent.Id))!.Progress);
        await _tasks.ChangeStatusAsync(b.Id, WorkTaskStatus.Completed);
        var p = (await _tasks.GetAsync(parent.Id))!;
        Assert.Equal(100, p.Progress);
        Assert.Equal(WorkTaskStatus.NotStarted, p.Status); // phải do người dùng xác nhận
        await Assert.ThrowsAsync<ValidationException>(() => _tasks.CreateSubtaskAsync(a.Id, New("Cấp 3")));
    }

    // ---------------------------------------------------------------- Phụ thuộc

    [Fact]
    public async Task Dependencies_block_priority_reject_cycles_and_unblock_when_done()
    {
        var a = await _tasks.CreateAsync(New("A – Thu thập tài liệu", 72));
        var b = await _tasks.CreateAsync(New("B – Soạn tờ trình", 24));
        await _tasks.AddDependencyAsync(b.Id, a.Id);
        await Assert.ThrowsAsync<ValidationException>(() => _tasks.AddDependencyAsync(a.Id, b.Id));

        var open = await _tasks.GetOpenTasksAsync();
        var ob = open.Single(t => t.Id == b.Id);
        Assert.True(ob.IsBlocked);
        Assert.Contains(a.Code, ob.BlockedBy);
        var assess = new PriorityEngine(_settings.Current).Assess(ob, _clock.Now);
        Assert.Contains(assess.Reasons, r => r.Text.Contains("Phải chờ") && r.Points < 0);

        await _tasks.ChangeStatusAsync(a.Id, WorkTaskStatus.Completed);
        Assert.False((await _tasks.GetOpenTasksAsync()).Single(t => t.Id == b.Id).IsBlocked);
    }

    [Fact]
    public void Scheduler_places_dependent_task_after_its_predecessor()
    {
        var now = _clock.Now;
        var pre = new WorkTask { Id = 1, Code = "A", Title = "A", ReceivedAt = now, CreatedAt = now, DueAt = now.AddDays(5), Priority = TaskPriority.Low, EstimatedHours = 3 };
        var dep = new WorkTask { Id = 2, Code = "B", Title = "B", ReceivedAt = now, CreatedAt = now, DueAt = now.AddDays(1), Priority = TaskPriority.Urgent, EstimatedHours = 2, DependsOnIds = new() { 1 }, BlockedBy = new() { "A" } };
        var p = new ScheduleEngine(new WorkSettings()).Propose(new[] { dep, pre }, now);
        var aEnd = p.Blocks.Where(x => x.Task.Id == 1).Max(x => x.End);
        var bStart = p.Blocks.Where(x => x.Task.Id == 2).Min(x => x.Start);
        Assert.True(bStart >= aEnd);
    }

    // ---------------------------------------------------------------- Dự án

    [Fact]
    public async Task Project_template_has_milestones_without_dates_and_progress_from_tasks()
    {
        var ps = new ProjectService(_factory, _clock);
        var p = await ps.CreateFromTemplateAsync(ProjectService.Templates[0]);
        Assert.StartsWith("DA-2026-", p.Code);
        var loaded = (await ps.GetAsync(p.Id))!;
        Assert.NotEmpty(loaded.Milestones);
        Assert.All(loaded.Milestones, m => { Assert.Null(m.DueDate); Assert.Null(m.CompletedAt); });
        Assert.Equal(ProjectStatus.Proposed, loaded.Status);

        var summary = (await ps.GetSummariesAsync()).Single();
        Assert.Null(summary.Progress); // chưa có công việc → không tự tạo tiến độ

        var t1 = New("Lập thuyết minh"); t1.ProjectId = p.Id;
        var t2 = New("Lập dự toán"); t2.ProjectId = p.Id; t2.Weight = 3;
        var c1 = await _tasks.CreateAsync(t1);
        await _tasks.CreateAsync(t2);
        await _tasks.ChangeStatusAsync(c1.Id, WorkTaskStatus.Completed);
        Assert.Equal(25, (await ps.GetSummariesAsync()).Single().Progress);
    }

    [Fact]
    public async Task Link_legacy_tasks_by_project_name()
    {
        var ps = new ProjectService(_factory, _clock);
        var t = New("Báo cáo tiến độ"); t.ProjectName = "nghien cuu che tao khang dien 500 kV";
        await _tasks.CreateAsync(t);
        var p = await ps.SaveAsync(new Project { Name = "Nghiên cứu chế tạo kháng điện 500 kV" });
        Assert.Equal(1, await ps.LinkByProjectNameAsync(p.Id));
    }

    // ---------------------------------------------------------------- Hồ sơ

    [Fact]
    public async Task Attachments_upload_validate_and_block_path_traversal()
    {
        var svc = new AttachmentService(_factory, _paths, _clock);
        var task = await _tasks.CreateAsync(New("Có hồ sơ"));
        await using var ms = new MemoryStream(Encoding.UTF8.GetBytes("noi dung"));
        var a = await svc.UploadAsync(ms, "Tờ trình phê duyệt.docx", ms.Length, task.Id, null);
        Assert.Equal(AttachmentKind.Proposal, a.Kind);
        Assert.True(File.Exists(svc.ResolveStoredPath(a.StoredFile!)));
        Assert.Single(await svc.SearchAsync("to trinh", null, null));

        await Assert.ThrowsAsync<ValidationException>(() => svc.UploadAsync(new MemoryStream(new byte[1]), "virus.exe", 1, task.Id, null));
        Assert.Null(svc.ResolveStoredPath("..\\..\\workmanager.db"));

        await svc.DeleteAsync(a.Id);
        Assert.Empty(await svc.GetForTaskAsync(task.Id));
    }

    // ---------------------------------------------------------------- Tài khoản

    [Fact]
    public async Task Users_password_rules_login_and_lockout()
    {
        var us = new UserService(_factory, _clock);
        Assert.Equal(LoginResult.NoUsers, (await us.LoginAsync("a", "b")).Result);
        await Assert.ThrowsAsync<ValidationException>(() => us.CreateFirstAdminAsync("admin", "Thư ký", "ngan"));
        await us.CreateFirstAdminAsync("ThuKy", "Thư ký KH&CN", "MatKhauManh2026");
        await Assert.ThrowsAsync<InvalidOperationException>(() => us.CreateFirstAdminAsync("khac", "x", "MatKhauManh2026"));
        Assert.Equal(LoginResult.Success, (await us.LoginAsync("thuky", "MatKhauManh2026")).Result);
        for (int i = 0; i < UserService.MaxFailedLogins - 1; i++)
            Assert.Equal(LoginResult.InvalidCredentials, (await us.LoginAsync("thuky", "sai")).Result);
        Assert.Equal(LoginResult.LockedOut, (await us.LoginAsync("thuky", "sai")).Result);
        Assert.Equal(LoginResult.LockedOut, (await us.LoginAsync("thuky", "MatKhauManh2026")).Result);
        _clock.Now = _clock.Now.AddMinutes(16);
        Assert.Equal(LoginResult.Success, (await us.LoginAsync("thuky", "MatKhauManh2026")).Result);
    }

    // ---------------------------------------------------------------- Báo cáo

    [Fact]
    public async Task Reports_use_only_recorded_data_and_export_to_all_formats()
    {
        var done = await _tasks.CreateAsync(New("Đã xong trong tuần", 4));
        await _tasks.ChangeStatusAsync(done.Id, WorkTaskStatus.Completed);
        var late = New("Quá hạn"); late.ReceivedAt = _clock.Now.AddDays(-3); late.DueAt = _clock.Now.AddDays(-1);
        await _tasks.CreateAsync(late);
        var wait = await _tasks.CreateAsync(New("Chờ phê duyệt", 72));
        await _tasks.ChangeStatusAsync(wait.Id, WorkTaskStatus.WaitingApproval);

        var rs = new ReportService(_factory, _clock, _settings);
        var week = await rs.WeeklyAsync(_clock.Now, new ReportNotes { Difficulties = "Chưa nhận được số liệu của Phòng KT" });
        Assert.Contains("hoàn thành 1 công việc", week.Sections[0].Paragraphs[0]);
        Assert.Single(week.Sections.Single(s => s.Heading.StartsWith("II.")).Table!.Rows);
        Assert.Single(week.Sections.Single(s => s.Heading.StartsWith("V.")).Table!.Rows);
        Assert.Contains(week.Sections, s => s.IsUserInput && s.Paragraphs.Contains("Chưa nhận được số liệu của Phòng KT"));

        var day = await rs.DailyAsync(_clock.Now);
        Assert.Single(day.Sections.Single(s => s.Heading.StartsWith("III.")).Table!.Rows);
        var month = await rs.MonthlyAsync(2026, 10);
        Assert.Contains(month.Sections, s => s.Heading.StartsWith("IV."));

        // Không có dữ liệu thì không tự tạo kết quả
        var empty = await rs.MonthlyAsync(2025, 1);
        Assert.Empty(empty.Sections.Single(s => s.Heading.StartsWith("III.")).Table!.Rows);

        Assert.True(ReportExporters.ToExcel(week).Length > 1000);
        var docx = ReportExporters.ToWord(week);
        Assert.Equal((byte)'P', docx[0]); // tệp zip (docx)
        var pdf = ReportExporters.ToPdf(week);
        Assert.Equal("%PDF", Encoding.ASCII.GetString(pdf, 0, 4));
    }

    // ---------------------------------------------------------------- Nhập dữ liệu desktop

    [Fact]
    public async Task Desktop_backup_import_is_idempotent()
    {
        // "Máy desktop" có dữ liệu riêng
        var desktopDir = Path.Combine(_dir, "desktop");
        var dPaths = new AppPaths(desktopDir);
        var dFactory = new TestDbFactory(dPaths.DatabasePath);
        await DbInitializer.InitializeAsync(dFactory);
        var dSettings = new SettingsService(dFactory);
        await dSettings.LoadAsync();
        var dTasks = new TaskService(dFactory, _clock, dSettings);
        var t = await dTasks.CreateAsync(New("Từ máy desktop"));
        await dTasks.AddCommentAsync(t.Id, "Ghi chú desktop");
        SqliteConnection.ClearAllPools();

        var webSync = new SyncService(_factory, _settings, new DeviceIdentity(Guid.NewGuid().ToString("N"), "Web"));
        var import = new DesktopImportService(webSync, _paths);
        var r1 = await import.ImportAsync(dPaths.DatabasePath);
        Assert.True(r1.Changes >= 2);
        var r2 = await import.ImportAsync(dPaths.DatabasePath);
        Assert.Equal(0, r2.Changes);

        var all = await _tasks.QueryAsync(new TaskQuery { StatusFilter = StatusFilter.All });
        var imported = Assert.Single(all.Items);
        Assert.Equal("Từ máy desktop", imported.Title);
        Assert.Contains(await _tasks.GetHistoryAsync(imported.Id), h => h.Detail == "Ghi chú desktop");
    }
}
