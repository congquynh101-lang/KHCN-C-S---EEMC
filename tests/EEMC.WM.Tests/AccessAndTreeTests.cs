using EEMC.WM.Core.Domain;
using EEMC.WM.Core.Time;
using EEMC.WM.Data;
using EEMC.WM.Data.Infrastructure;
using EEMC.WM.Data.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace EEMC.WM.Tests;

public sealed class AccessAndTreeTests : IAsyncLifetime
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "eemc-wm-access", Guid.NewGuid().ToString("N"));
    private readonly FixedClock _clock = new(new DateTime(2026, 10, 21, 9, 0, 0));
    private AppPaths _paths = null!;
    private TestDbFactory _factory = null!;
    private TaskService _tasks = null!;
    private UserService _users = null!;

    private static readonly Actor Viewer = new(10, "Người xem", UserRole.Viewer);
    private static readonly Actor Editor = new(11, "Biên tập viên", UserRole.Editor);
    private static readonly Actor Admin = new(12, "Quản trị", UserRole.Admin);

    public async Task InitializeAsync()
    {
        _paths = new AppPaths(_dir);
        _factory = new TestDbFactory(_paths.DatabasePath);
        await DbInitializer.InitializeAsync(_factory);
        var settings = new SettingsService(_factory);
        await settings.LoadAsync();
        _tasks = new TaskService(_factory, _clock, settings);
        _users = new UserService(_factory, _clock);
    }

    public Task DisposeAsync()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, true); } catch { }
        return Task.CompletedTask;
    }

    private WorkTask New(string title, int? projectId = null) => new()
    {
        Title = title, ReceivedAt = _clock.Now, DueAt = _clock.Now.AddDays(3), Priority = TaskPriority.Normal, ProjectId = projectId
    };

    // ---------------------------------------------------------------- Phân quyền trong dịch vụ

    [Fact]
    public async Task Service_writes_are_checked_against_the_role_in_scope()
    {
        using (Access.Use(Viewer))
        {
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _tasks.CreateAsync(New("Không được tạo")));
            Assert.False(Access.Can(UserRole.Editor));
        }
        WorkTask t;
        using (Access.Use(Editor))
        {
            t = await _tasks.CreateAsync(New("Biên tập tạo được"));
            await _tasks.MoveToTrashAsync(new[] { t.Id });
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _tasks.PurgeAsync(new[] { t.Id }));
        }
        using (Access.Use(Admin)) await _tasks.PurgeAsync(new[] { t.Id });
        // Phạm vi mở nhưng không có người dùng (phiên hết hạn): từ chối
        using (Access.Use(null)) await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _tasks.CreateAsync(New("x")));
        // Ngoài mọi phạm vi (bản desktop, kiểm thử): chạy như hệ thống
        Assert.NotNull(await _tasks.CreateAsync(New("Desktop")));
    }

    [Fact]
    public async Task Admin_manages_users_and_last_admin_is_protected()
    {
        var admin = await _users.CreateFirstAdminAsync("quantri", "Quản trị", "MatKhauManh2026");
        using (Access.Use(Viewer)) await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _users.GetAllAsync());

        using (Access.Use(new Actor(admin.Id, admin.DisplayName, UserRole.Admin)))
        {
            var u = await _users.CreateUserAsync("bientap", "Biên tập", "MatKhauManh2026", UserRole.Editor);
            Assert.Equal(UserRole.Editor, (await _users.GetActorAsync(u.Id))!.Role);

            await _users.UpdateUserAsync(u.Id, "Biên tập", UserRole.Viewer, false);
            Assert.Equal(UserRole.Viewer, (await _users.GetActorAsync(u.Id))!.Role); // bộ đệm được làm mới

            await _users.UpdateUserAsync(u.Id, "Biên tập", UserRole.Viewer, true);
            Assert.Null(await _users.GetActorAsync(u.Id));
            Assert.Equal(LoginResult.Disabled, (await _users.LoginAsync("bientap", "MatKhauManh2026")).Result);

            await Assert.ThrowsAsync<ValidationException>(() => _users.UpdateUserAsync(admin.Id, "Quản trị", UserRole.Editor, false));
            await Assert.ThrowsAsync<ValidationException>(() => _users.UpdateUserAsync(admin.Id, "Quản trị", UserRole.Admin, true));
        }
        // Người xem chỉ đổi được mật khẩu của chính mình
        using (Access.Use(new Actor(999, "Khác", UserRole.Viewer)))
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _users.ChangePasswordAsync(admin.Id, "MatKhauManh2026", "MatKhauMoi2026x"));
    }

    [Fact]
    public async Task Upgrade_maps_old_admin_flag_to_roles()
    {
        var dir = Path.Combine(_dir, "nang-cap");
        var factory = new TestDbFactory(new AppPaths(dir).DatabasePath);
        await using (var db = factory.CreateDbContext())
        {
            var migrator = db.GetService<IMigrator>();
            await migrator.MigrateAsync("20261021114618_LegalDocuments"); // CSDL trước bản phân quyền
            await db.Database.ExecuteSqlRawAsync(
                "INSERT INTO Users (UserName, DisplayName, PasswordHash, IsAdmin, FailedLogins, CreatedAt) VALUES " +
                "('cu.quantri','A','x',1,0,'2026-10-01'), ('cu.nguoidung','B','x',0,0,'2026-10-01')");
            await migrator.MigrateAsync();
        }
        await using (var db = factory.CreateDbContext())
        {
            var roles = await db.Users.AsNoTracking().ToDictionaryAsync(u => u.UserName, u => u.Role);
            Assert.Equal(UserRole.Admin, roles["cu.quantri"]);
            Assert.Equal(UserRole.Editor, roles["cu.nguoidung"]);
        }
    }

    // ---------------------------------------------------------------- Cây: tải từng cấp

    [Fact]
    public async Task Task_tree_loads_one_level_with_child_counts_and_closed_filter()
    {
        var projects = new ProjectService(_factory, _clock);
        var p = await projects.SaveAsync(new Project { Name = "Dự án mẫu" });
        var root = await _tasks.CreateAsync(New("Gốc", p.Id));
        var c1 = await _tasks.CreateSubtaskAsync(root.Id, New("Con 1"));
        var c2 = await _tasks.CreateSubtaskAsync(root.Id, New("Con 2"));
        await _tasks.CreateSubtaskAsync(c1.Id, New("Cháu"));
        await _tasks.ChangeStatusAsync(c2.Id, WorkTaskStatus.Completed);
        await _tasks.CreateAsync(New("Không thuộc dự án"));

        var counts = await _tasks.CountRootTasksByProjectAsync(includeClosed: false);
        Assert.Equal(1, counts[p.Id]);
        Assert.Equal(1, counts[0]);

        var top = await _tasks.GetTreeLevelAsync(p.Id, null, includeClosed: false);
        Assert.Equal(root.Id, Assert.Single(top).Task.Id);
        Assert.Equal(1, top[0].ChildCount); // Con 2 đã hoàn thành không tính

        var children = await _tasks.GetTreeLevelAsync(null, root.Id, includeClosed: true);
        Assert.Equal(2, children.Count);
        Assert.Equal(1, children.Single(x => x.Task.Id == c1.Id).ChildCount);
        Assert.Equal(0, children.Single(x => x.Task.Id == c2.Id).ChildCount);
    }

    [Fact]
    public async Task Legal_tree_outline_and_article_search()
    {
        var legal = new LegalService(_factory, _paths, _clock);
        var d = await legal.SaveAsync(new LegalDocument
        {
            Title = "Văn bản mẫu", DocType = LegalDocType.Circular,
            FullText = "Chương I\nCHUNG\nĐiều 1. Phạm vi\nNội dung về hồ sơ mẫu.\nĐiều 2. Đối tượng\nDoanh nghiệp."
        });
        Assert.Equal(1, (await legal.CountByTypeAsync())[LegalDocType.Circular]);
        var (doc, sections) = Assert.Single(await legal.GetByTypeAsync(LegalDocType.Circular));
        Assert.Equal(3, sections);
        var outline = await legal.GetOutlineAsync(d.Id);
        Assert.All(outline, s => Assert.Equal("", s.Body)); // mục lục không kèm nội dung
        var hit = Assert.Single(await legal.SearchArticlesAsync("ho so"));
        Assert.Equal(outline.Single(s => s.Number == "1").SortOrder, hit.SortOrder);
    }
}
