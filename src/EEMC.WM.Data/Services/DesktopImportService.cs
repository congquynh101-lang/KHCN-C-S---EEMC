using EEMC.WM.Data.Infrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace EEMC.WM.Data.Services;

public sealed record DesktopImportResult(int Batches, int Changes, int CodesRenamed);

/// <summary>
/// Nhập dữ liệu từ bản desktop (tệp sao lưu .db) vào bản web. Dùng lại cơ chế đồng bộ:
/// bản sửa mới hơn được giữ, nhập lại nhiều lần không bị nhân đôi.
/// </summary>
public sealed class DesktopImportService
{
    private readonly SyncService _target;
    private readonly AppPaths _paths;

    public DesktopImportService(SyncService target, AppPaths paths)
    {
        _target = target;
        _paths = paths;
    }

    private sealed class FileFactory : IDbContextFactory<AppDbContext>
    {
        private readonly DbContextOptions<AppDbContext> _o;
        public FileFactory(string path) => _o = new DbContextOptionsBuilder<AppDbContext>().UseSqlite($"Data Source={path};Pooling=False").Options;
        public AppDbContext CreateDbContext() => new(_o);
    }

    public async Task<DesktopImportResult> ImportAsync(string uploadedDbPath)
    {
        if (!BackupService.IsValidBackup(uploadedDbPath, out var err)) throw new InvalidOperationException(err);

        // Làm việc trên bản sao để không thay đổi tệp người dùng tải lên
        var work = Path.Combine(_paths.DataDir, "import-tmp");
        Directory.CreateDirectory(work);
        var copy = Path.Combine(work, $"desktop-{Guid.NewGuid():N}.db");
        File.Copy(uploadedDbPath, copy);
        try
        {
            var factory = new FileFactory(copy);
            await DbInitializer.InitializeAsync(factory);   // nâng cấp lược đồ bản 1.0 lên bản hiện tại
            var settings = new SettingsService(factory);
            await settings.LoadAsync();
            var source = new SyncService(factory, settings, new DeviceIdentity(Guid.NewGuid().ToString("N"), "Desktop"));
            var batches = await source.BuildSnapshotBatchesAsync();
            int changes = 0;
            foreach (var b in batches) changes += await _target.ApplyBatchAsync(b);
            var renamed = await _target.ResolveCodeCollisionsAsync();
            AppLog.Info($"Nhập dữ liệu desktop: {batches.Count} gói, {changes} thay đổi, {renamed} mã đổi.");
            return new DesktopImportResult(batches.Count, changes, renamed);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            try { File.Delete(copy); } catch { /* dọn lần sau */ }
        }
    }
}
