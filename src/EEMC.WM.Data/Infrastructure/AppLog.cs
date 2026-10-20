using System.Text;

namespace EEMC.WM.Data.Infrastructure;

/// <summary>Ghi nhật ký lỗi ra tệp logs\yyyy-MM-dd.log (giữ 60 ngày).</summary>
public static class AppLog
{
    private static readonly object Gate = new();
    private static string? _dir;

    public static void Initialize(string logsDir)
    {
        _dir = logsDir;
        try
        {
            Directory.CreateDirectory(logsDir);
            foreach (var f in Directory.GetFiles(logsDir, "*.log"))
                if (File.GetLastWriteTime(f) < DateTime.Now.AddDays(-60)) File.Delete(f);
        }
        catch { /* không chặn khởi động vì nhật ký */ }
    }

    public static void Info(string message) => Write("INFO", message, null);
    public static void Warn(string message) => Write("WARN", message, null);
    public static void Error(Exception ex, string context) => Write("ERROR", context, ex);

    private static void Write(string level, string message, Exception? ex)
    {
        if (_dir == null) return;
        try
        {
            var sb = new StringBuilder();
            sb.Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")).Append(" [").Append(level).Append("] ").AppendLine(message);
            if (ex != null) sb.AppendLine(ex.ToString());
            lock (Gate)
                File.AppendAllText(Path.Combine(_dir, DateTime.Now.ToString("yyyy-MM-dd") + ".log"), sb.ToString(), Encoding.UTF8);
        }
        catch { /* bỏ qua lỗi ghi nhật ký */ }
    }
}
