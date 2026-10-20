using EEMC.WM.Data.Infrastructure;
using Microsoft.Data.Sqlite;

namespace EEMC.WM.Data.Services;

public sealed record BackupInfo(string Path, DateTime CreatedAt, long SizeBytes)
{
    public string FileName => System.IO.Path.GetFileName(Path);
    public string SizeText => SizeBytes < 1024 * 1024 ? $"{SizeBytes / 1024.0:0.#} KB" : $"{SizeBytes / 1024.0 / 1024.0:0.##} MB";
}

/// <summary>Sao lưu (SQLite Online Backup API, an toàn khi đang chạy) và phục hồi dữ liệu.</summary>
public sealed class BackupService
{
    private const string Prefix = "EEMC-WM-";
    private readonly AppPaths _paths;
    private readonly SettingsService _settings;

    public BackupService(AppPaths paths, SettingsService settings)
    {
        _paths = paths;
        _settings = settings;
    }

    public string BackupFolder =>
        string.IsNullOrWhiteSpace(_settings.Current.BackupFolder) ? _paths.DefaultBackupDir : _settings.Current.BackupFolder!;

    public async Task<BackupInfo> BackupNowAsync(string? folder = null, string suffix = "")
    {
        folder ??= BackupFolder;
        Directory.CreateDirectory(folder);
        var file = Path.Combine(folder, $"{Prefix}{DateTime.Now:yyyyMMdd-HHmmss}{suffix}.db");
        await Task.Run(() =>
        {
            using var src = new SqliteConnection($"Data Source={_paths.DatabasePath}");
            using var dst = new SqliteConnection($"Data Source={file};Pooling=False");
            src.Open();
            dst.Open();
            src.BackupDatabase(dst);
        });
        SqliteConnection.ClearAllPools();
        var fi = new FileInfo(file);
        AppLog.Info($"Đã sao lưu dữ liệu: {file}");
        Cleanup(folder);
        return new BackupInfo(file, fi.CreationTime, fi.Length);
    }

    /// <summary>Tự động sao lưu một lần mỗi ngày khi mở ứng dụng.</summary>
    public async Task<BackupInfo?> AutoBackupIfNeededAsync()
    {
        if (!_settings.Current.AutoBackupDaily || !File.Exists(_paths.DatabasePath)) return null;
        try
        {
            var folder = BackupFolder;
            if (Directory.Exists(folder) &&
                Directory.GetFiles(folder, $"{Prefix}*.db").Any(f => File.GetCreationTime(f).Date == DateTime.Today))
                return null;
            return await BackupNowAsync(folder, "-auto");
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "Tự động sao lưu thất bại");
            return null;
        }
    }

    public List<BackupInfo> ListBackups()
    {
        var folder = BackupFolder;
        if (!Directory.Exists(folder)) return new List<BackupInfo>();
        return Directory.GetFiles(folder, $"{Prefix}*.db")
            .Select(f => new FileInfo(f))
            .OrderByDescending(f => f.CreationTime)
            .Select(f => new BackupInfo(f.FullName, f.CreationTime, f.Length))
            .ToList();
    }

    private void Cleanup(string folder)
    {
        try
        {
            var keep = Math.Max(1, _settings.Current.BackupKeepCount);
            // Chỉ dọn các bản tự động; bản sao lưu thủ công giữ nguyên
            var autos = Directory.GetFiles(folder, $"{Prefix}*-auto.db")
                .Select(f => new FileInfo(f)).OrderByDescending(f => f.CreationTime).Skip(keep);
            foreach (var f in autos) f.Delete();
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "Dọn bản sao lưu cũ thất bại");
        }
    }

    /// <summary>Kiểm tra tệp có phải CSDL hợp lệ của phần mềm không.</summary>
    public static bool IsValidBackup(string file, out string error)
    {
        error = "";
        if (!File.Exists(file)) { error = "Không tìm thấy tệp."; return false; }
        try
        {
            using var c = new SqliteConnection($"Data Source={file};Mode=ReadOnly;Pooling=False");
            c.Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name IN ('WorkTasks','Categories','__EFMigrationsHistory');";
            var n = Convert.ToInt32(cmd.ExecuteScalar());
            if (n < 3) { error = "Tệp không phải dữ liệu của EEMC KHCN Work Manager."; return false; }
            using var chk = c.CreateCommand();
            chk.CommandText = "PRAGMA integrity_check;";
            var r = chk.ExecuteScalar()?.ToString();
            if (!string.Equals(r, "ok", StringComparison.OrdinalIgnoreCase)) { error = "Tệp sao lưu bị lỗi: " + r; return false; }
            return true;
        }
        catch (Exception ex)
        {
            error = "Không đọc được tệp: " + ex.Message;
            return false;
        }
    }

    /// <summary>
    /// Phục hồi từ tệp sao lưu: sao lưu dữ liệu hiện tại trước (hậu tố -truoc-phuc-hoi), sau đó ghi đè.
    /// Ứng dụng cần khởi động lại sau khi phục hồi.
    /// </summary>
    public async Task<BackupInfo> RestoreAsync(string backupFile)
    {
        if (!IsValidBackup(backupFile, out var err)) throw new InvalidOperationException(err);
        var safety = await BackupNowAsync(BackupFolder, "-truoc-phuc-hoi");
        await Task.Run(() =>
        {
            SqliteConnection.ClearAllPools();
            using var src = new SqliteConnection($"Data Source={backupFile};Mode=ReadOnly;Pooling=False");
            using var dst = new SqliteConnection($"Data Source={_paths.DatabasePath};Pooling=False");
            src.Open();
            dst.Open();
            src.BackupDatabase(dst);
        });
        SqliteConnection.ClearAllPools();
        AppLog.Info($"Đã phục hồi dữ liệu từ {backupFile}. Bản an toàn: {safety.Path}");
        return safety;
    }
}
