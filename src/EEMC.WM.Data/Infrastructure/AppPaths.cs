namespace EEMC.WM.Data.Infrastructure;

/// <summary>Vị trí lưu dữ liệu cục bộ. Có thể chỉ định thư mục khác bằng biến môi trường EEMC_WM_DATA.</summary>
public sealed class AppPaths
{
    public AppPaths(string? dataDir = null)
    {
        var custom = dataDir ?? Environment.GetEnvironmentVariable("EEMC_WM_DATA");
        IsCustomDataDir = !string.IsNullOrWhiteSpace(custom);
        DataDir = IsCustomDataDir
            ? custom!
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EEMC", "WorkManager");
        Directory.CreateDirectory(DataDir);
        Directory.CreateDirectory(LogsDir);
    }

    public string DataDir { get; }
    public bool IsCustomDataDir { get; }
    public string DatabasePath => Path.Combine(DataDir, "workmanager.db");
    public string LogsDir => Path.Combine(DataDir, "logs");

    /// <summary>
    /// Thư mục sao lưu mặc định: Documents\EEMC WorkManager\SaoLuu.
    /// Khi dùng thư mục dữ liệu riêng (EEMC_WM_DATA), sao lưu nằm cạnh dữ liệu để không lẫn với dữ liệu thật.
    /// </summary>
    public string DefaultBackupDir => IsCustomDataDir
        ? Path.Combine(DataDir, "SaoLuu")
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "EEMC WorkManager", "SaoLuu");

    public string ConnectionString => $"Data Source={DatabasePath};Default Timeout=30";
}
