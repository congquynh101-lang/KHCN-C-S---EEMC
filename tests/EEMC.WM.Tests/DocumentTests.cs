using System.Text;
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

public sealed class DocumentTests : IAsyncLifetime
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "eemc-wm-docs", Guid.NewGuid().ToString("N"));
    private readonly FixedClock _clock = new(new DateTime(2026, 10, 22, 9, 0, 0));
    private AppPaths _paths = null!;
    private TestDbFactory _factory = null!;
    private TaskService _tasks = null!;
    private AttachmentService _files = null!;
    private DocumentService _docs = null!;

    public async Task InitializeAsync()
    {
        _paths = new AppPaths(_dir);
        _factory = new TestDbFactory(_paths.DatabasePath);
        await DbInitializer.InitializeAsync(_factory);
        var settings = new SettingsService(_factory);
        await settings.LoadAsync();
        _tasks = new TaskService(_factory, _clock, settings);
        _files = new AttachmentService(_factory, _paths, _clock);
        _docs = new DocumentService(_factory, _files, _clock);
    }

    public Task DisposeAsync()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, true); } catch { }
        return Task.CompletedTask;
    }

    private Task<WorkTask> NewTask(string title, int? projectId = null) => _tasks.CreateAsync(new WorkTask
    {
        Title = title, ReceivedAt = _clock.Now, DueAt = _clock.Now.AddDays(5), Priority = TaskPriority.Normal, ProjectId = projectId
    });

    private Task<UploadResult> Upload(string name, string content, DocTarget target)
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        return _files.UploadAsync(new MemoryStream(bytes), name, bytes.Length, target);
    }

    private int StoredFileCount() => Directory.Exists(_files.FilesDir) ? Directory.GetFiles(_files.FilesDir, "*", SearchOption.AllDirectories).Length : 0;

    // ---------------------------------------------------------------- Không nhân bản tệp

    [Fact]
    public async Task Same_content_is_stored_once_and_placed_in_many_places()
    {
        var p = await new ProjectService(_factory, _clock).SaveAsync(new Project { Name = "Dự án mẫu" });
        var t = await NewTask("Công việc mẫu", p.Id);
        var first = await Upload("Biên bản họp.txt", "Nội dung biên bản nghiệm thu mẫu", DocTarget.Task(t.Id));
        Assert.False(first.Duplicate);
        var second = await Upload("ban-sao.txt", "Nội dung biên bản nghiệm thu mẫu", DocTarget.Project(p.Id));
        Assert.True(second.Duplicate);
        Assert.Equal(first.Attachment.Id, second.Attachment.Id);
        Assert.Equal(1, StoredFileCount());

        Assert.Single(await _files.GetForTaskAsync(t.Id));
        Assert.Single(await _files.GetForProjectAsync(p.Id));
        Assert.Equal(2, (await _files.GetPlacesAsync(first.Attachment.Id)).Count);

        // Tìm theo nội dung (không dấu) và theo tên công việc chứa tài liệu
        Assert.Single(await _files.SearchAsync("nghiem thu", null, null));
        Assert.Single(await _files.SearchAsync("cong viec mau", null, null));
    }

    // ---------------------------------------------------------------- Phiên bản

    [Fact]
    public async Task Versions_keep_old_files_and_restore_reuses_them()
    {
        var t = await NewTask("Có phiên bản");
        var a = (await Upload("De cuong.txt", "ban 1", DocTarget.Task(t.Id))).Attachment;
        var bytes = Encoding.UTF8.GetBytes("ban 2 da sua");
        await _files.UploadVersionAsync(a.Id, new MemoryStream(bytes), "De cuong v2.txt", bytes.Length, "Sửa theo góp ý");
        await Assert.ThrowsAsync<ValidationException>(() =>
            _files.UploadVersionAsync(a.Id, new MemoryStream(bytes), "trung.txt", bytes.Length, null)); // giống bản hiện hành

        var cur = (await _files.GetAsync(a.Id))!;
        Assert.Equal(2, cur.CurrentVersion);
        Assert.Contains("ban 2", cur.ExtractedText);
        Assert.Equal(2, StoredFileCount());

        await _files.RestoreVersionAsync(a.Id, 1);
        cur = (await _files.GetAsync(a.Id))!;
        Assert.Equal(3, cur.CurrentVersion);
        Assert.Equal("ban 1", cur.ExtractedText);
        Assert.Equal(2, StoredFileCount()); // khôi phục không sao chép tệp
        Assert.Equal(new[] { 3, 2, 1 }, (await _files.GetVersionsAsync(a.Id)).Select(v => v.VersionNo));

        // Xóa vĩnh viễn: xóa mọi tệp của các phiên bản
        await _docs.TrashFileAsync(a.Id);
        await _docs.PurgeFileAsync(a.Id);
        Assert.Equal(0, StoredFileCount());
    }

    // ---------------------------------------------------------------- Thư mục

    [Fact]
    public async Task Folders_create_rename_move_trash_restore_purge()
    {
        var t = await NewTask("Có thư mục");
        var root = await _docs.CreateFolderAsync("Hồ sơ nghiệm thu", null, new DocOwner(TaskId: t.Id));
        var sub = await _docs.CreateFolderAsync("Biên bản", root.Id, DocOwner.Common); // chủ sở hữu lấy theo thư mục cha
        Assert.Equal(t.Id, sub.TaskId);
        await Assert.ThrowsAsync<ValidationException>(() => _docs.CreateFolderAsync("ho so nghiem thu", null, new DocOwner(TaskId: t.Id))); // trùng tên (không dấu)
        await Assert.ThrowsAsync<ValidationException>(() => _docs.MoveFolderAsync(root.Id, sub.Id, DocOwner.Common)); // vào chính thư mục con

        var only = (await Upload("chi-o-day.txt", "chỉ trong thư mục", DocTarget.Folder(sub.Id))).Attachment;
        var shared = (await Upload("hai-noi.txt", "ở hai nơi", DocTarget.Folder(sub.Id))).Attachment;
        await _docs.AddPlacementAsync(shared.Id, DocTarget.Task(t.Id));
        Assert.Equal(2, (await _files.GetForTaskAsync(t.Id)).Count); // gồm cả tài liệu trong thư mục của công việc

        // Chuyển cả cây sang thư mục chung
        await _docs.MoveFolderAsync(root.Id, null, DocOwner.Common);
        Assert.Null((await _docs.GetFolderAsync(sub.Id))!.TaskId);
        await _docs.RenameFolderAsync(root.Id, "Hồ sơ nghiệm thu 2026");

        // Xóa vào thùng rác: tài liệu chỉ ở trong thư mục vào thùng rác, tài liệu còn nơi khác thì giữ
        await _docs.TrashFolderAsync(root.Id);
        Assert.True((await _files.GetAsync(only.Id))!.IsDeleted);
        Assert.False((await _files.GetAsync(shared.Id))!.IsDeleted);
        var trash = await _docs.GetTrashAsync();
        Assert.Equal(root.Id, Assert.Single(trash.Folders).Id);

        await _docs.RestoreFolderAsync(root.Id);
        Assert.False((await _files.GetAsync(only.Id))!.IsDeleted);
        Assert.False((await _docs.GetFolderAsync(sub.Id))!.IsDeleted);

        await _docs.TrashFolderAsync(root.Id);
        await _docs.PurgeFolderAsync(root.Id);
        Assert.Null(await _docs.GetFolderAsync(sub.Id));
        Assert.Null(await _files.GetAsync(only.Id));   // không còn nơi nào khác → xóa hẳn
        Assert.NotNull(await _files.GetAsync(shared.Id)); // vẫn ở công việc
    }

    [Fact]
    public async Task Removing_last_place_trashes_and_purging_owner_keeps_files()
    {
        var t = await NewTask("Sẽ bị xóa");
        var f = await _docs.CreateFolderAsync("Tài liệu", null, new DocOwner(TaskId: t.Id));
        var a = (await Upload("a.txt", "tài liệu a", DocTarget.Task(t.Id))).Attachment;
        await Upload("b.txt", "tài liệu b", DocTarget.Folder(f.Id));

        var link = Assert.Single(await _files.GetPlacesAsync(a.Id));
        Assert.True(await _docs.RemovePlacementAsync(link.LinkId));
        Assert.True((await _files.GetAsync(a.Id))!.IsDeleted);
        await Assert.ThrowsAsync<ValidationException>(() => _docs.RestoreFileAsync(a.Id)); // không còn nơi đặt

        await _tasks.MoveToTrashAsync(new[] { t.Id });
        await _tasks.PurgeAsync(new[] { t.Id });
        var folder = (await _docs.GetFolderAsync(f.Id))!;
        Assert.Null(folder.TaskId);                 // thành thư mục chung
        Assert.StartsWith("[CV-", folder.Name);     // còn nhận ra từ công việc nào
        Assert.Single((await _docs.GetContentAsync(DocOwner.Common, f.Id)).Files);
    }

    [Fact]
    public async Task Legal_documents_hold_files_and_decisions_hold_folders()
    {
        var legal = new LegalService(_factory, _paths, _clock);
        var guide = await legal.SaveAsync(new LegalDocument { Title = "Hướng dẫn mẫu", DocType = LegalDocType.Guidance });
        var decision = await legal.SaveAsync(new LegalDocument { Title = "Quyết định mẫu", DocType = LegalDocType.Decision });
        await Upload("phu-luc.txt", "phụ lục hướng dẫn", DocTarget.Legal(guide.Id));
        var row = Assert.Single(await _files.GetRowsForAsync(new DocOwner(LegalDocumentId: guide.Id)));
        Assert.Contains("Hướng dẫn mẫu", Assert.Single(row.Places).Label);
        // Thư mục chỉ tạo cho quyết định (nhánh "Theo quyết định" trên cây hồ sơ)
        await _docs.CreateFolderAsync("Hồ sơ thực hiện", null, new DocOwner(LegalDocumentId: decision.Id));
        await Assert.ThrowsAsync<InvalidOperationException>(() => _docs.CreateFolderAsync("Không được", null, new DocOwner(LegalDocumentId: guide.Id)));
    }

    [Fact]
    public async Task Viewer_cannot_change_documents()
    {
        var t = await NewTask("Phân quyền");
        using (Access.Use(new Actor(5, "Người xem", UserRole.Viewer)))
        {
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _docs.CreateFolderAsync("X", null, DocOwner.Common));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Upload("x.txt", "x", DocTarget.Task(t.Id)));
        }
        using (Access.Use(new Actor(6, "Biên tập", UserRole.Editor)))
        {
            var a = (await Upload("y.txt", "y", DocTarget.Task(t.Id))).Attachment;
            Assert.Equal("Biên tập", a.UploadedBy);
            await _docs.TrashFileAsync(a.Id);
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _docs.PurgeFileAsync(a.Id)); // xóa vĩnh viễn: quản trị
        }
    }

    // ---------------------------------------------------------------- Nâng cấp dữ liệu cũ

    [Fact]
    public async Task Upgrade_moves_old_attachments_to_places_and_versions()
    {
        var factory = new TestDbFactory(new AppPaths(Path.Combine(_dir, "cu")).DatabasePath);
        await using (var db = factory.CreateDbContext())
        {
            var m = db.GetService<IMigrator>();
            await m.MigrateAsync("20261021135120_UserRoles");
            await db.Database.ExecuteSqlRawAsync(
                "INSERT INTO Projects (Code, Name, Status, IsArchived, CreatedAt, UpdatedAt) VALUES ('DA-1','Dự án',1,0,'2026-10-01','2026-10-01');" +
                "INSERT INTO Attachments (TaskId, ProjectId, Name, Kind, StoredFile, SizeBytes, DocumentStatus, CreatedAt) VALUES " +
                "(NULL, 1, 'Đề cương.pdf', 3, '2026/10/a.pdf', 100, 0, '2026-10-01')," +
                "(NULL, NULL, 'Mồ côi.pdf', 3, '2026/10/b.pdf', 50, 0, '2026-10-02')," +
                "(NULL, 1, 'Thư mục mạng', 4, NULL, 0, 0, '2026-10-03');");
            await m.MigrateAsync();
        }
        await using (var db = factory.CreateDbContext())
        {
            Assert.Equal(2, await db.AttachmentLinks.CountAsync(l => l.ProjectId == 1));
            Assert.Equal(2, await db.AttachmentVersions.CountAsync());
            var orphan = await db.Attachments.SingleAsync(a => a.Name == "Mồ côi.pdf");
            Assert.True(orphan.IsDeleted); // vào thùng rác, tệp vẫn giữ
            Assert.Null((await db.Attachments.SingleAsync(a => a.Name == "Đề cương.pdf")).ExtractedText); // chờ đọc nội dung
        }
    }
}
