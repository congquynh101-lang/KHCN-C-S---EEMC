using EEMC.WM.Core.Domain;
using EEMC.WM.Core.Text;
using EEMC.WM.Core.Time;
using EEMC.WM.Data.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace EEMC.WM.Data.Services;

public sealed class AttachmentRow
{
    public required Attachment Attachment { get; init; }
    public string? TaskCode { get; init; }
    public string? TaskTitle { get; init; }
    public string? ProjectName { get; init; }
}

/// <summary>
/// Hồ sơ, tài liệu: lưu tệp tải lên trong thư mục dữ liệu (files\yyyy\MM\...) hoặc chỉ lưu đường dẫn/liên kết.
/// Tệp gốc không bao giờ bị sửa; người dùng tải về và mở bằng ứng dụng mặc định.
/// </summary>
public sealed class AttachmentService
{
    public const long MaxBytes = 50L * 1024 * 1024;

    public static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx", ".pdf", ".txt", ".rtf", ".csv",
        ".jpg", ".jpeg", ".png", ".gif", ".zip", ".rar", ".7z", ".msg", ".eml", ".dwg", ".odt", ".ods"
    };

    private readonly IDbContextFactory<AppDbContext> _factory;
    private readonly AppPaths _paths;
    private readonly IClock _clock;

    public AttachmentService(IDbContextFactory<AppDbContext> factory, AppPaths paths, IClock clock)
    {
        _factory = factory;
        _paths = paths;
        _clock = clock;
    }

    public string FilesDir => Path.Combine(_paths.DataDir, "files");

    public static AttachmentKind GuessKind(string fileName)
    {
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        var n = TextNormalizer.Normalize(fileName);
        if (n.Contains("bien ban")) return AttachmentKind.Minutes;
        if (n.Contains("quyet dinh") || n.StartsWith("qd")) return AttachmentKind.Decision;
        if (n.Contains("to trinh") || n.StartsWith("tt")) return AttachmentKind.Proposal;
        if (n.Contains("bao cao") || n.StartsWith("bc")) return AttachmentKind.Report;
        return ext switch
        {
            ".doc" or ".docx" or ".odt" or ".rtf" => AttachmentKind.Word,
            ".xls" or ".xlsx" or ".ods" or ".csv" => AttachmentKind.Excel,
            ".pdf" => AttachmentKind.Pdf,
            _ => AttachmentKind.Other
        };
    }

    public async Task<List<Attachment>> GetForTaskAsync(int taskId)
    {
        await using var db = await _factory.CreateDbContextAsync();
        return await db.Attachments.AsNoTracking().Where(a => a.TaskId == taskId).OrderByDescending(a => a.CreatedAt).ToListAsync();
    }

    public async Task<List<Attachment>> GetForProjectAsync(int projectId)
    {
        await using var db = await _factory.CreateDbContextAsync();
        return await db.Attachments.AsNoTracking().Where(a => a.ProjectId == projectId).OrderByDescending(a => a.CreatedAt).ToListAsync();
    }

    public async Task<Attachment?> GetAsync(int id)
    {
        await using var db = await _factory.CreateDbContextAsync();
        return await db.Attachments.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id);
    }

    /// <summary>Tìm kiếm hồ sơ theo tên tài liệu, số văn bản, tên công việc, dự án.</summary>
    public async Task<List<AttachmentRow>> SearchAsync(string? text, AttachmentKind? kind, DocumentStatus? status, int take = 300)
    {
        await using var db = await _factory.CreateDbContextAsync();
        var q = db.Attachments.AsNoTracking().AsQueryable();
        if (kind.HasValue) q = q.Where(a => a.Kind == kind.Value);
        if (status.HasValue) q = q.Where(a => a.DocumentStatus == status.Value);
        var rows = await q.OrderByDescending(a => a.CreatedAt).Take(5000).ToListAsync();
        var taskIds = rows.Where(a => a.TaskId != null).Select(a => a.TaskId!.Value).Distinct().ToList();
        var projIds = rows.Where(a => a.ProjectId != null).Select(a => a.ProjectId!.Value).Distinct().ToList();
        var tasks = await db.Tasks.AsNoTracking().Where(t => taskIds.Contains(t.Id)).Select(t => new { t.Id, t.Code, t.Title, t.ProjectName }).ToDictionaryAsync(t => t.Id);
        var projects = await db.Projects.AsNoTracking().Where(p => projIds.Contains(p.Id)).Select(p => new { p.Id, p.Name }).ToDictionaryAsync(p => p.Id);
        var result = rows.Select(a =>
        {
            var t = a.TaskId.HasValue && tasks.TryGetValue(a.TaskId.Value, out var tt) ? tt : null;
            var p = a.ProjectId.HasValue && projects.TryGetValue(a.ProjectId.Value, out var pp) ? pp : null;
            return new AttachmentRow { Attachment = a, TaskCode = t?.Code, TaskTitle = t?.Title, ProjectName = p?.Name ?? t?.ProjectName };
        });
        if (!string.IsNullOrWhiteSpace(text))
        {
            var terms = TextNormalizer.Normalize(text).Split(' ', StringSplitOptions.RemoveEmptyEntries);
            result = result.Where(r =>
            {
                var hay = TextNormalizer.Normalize(string.Join(" ", r.Attachment.Name, r.Attachment.DocumentNumber, r.Attachment.Link, r.TaskCode, r.TaskTitle, r.ProjectName));
                return terms.All(hay.Contains);
            });
        }
        return result.Take(take).ToList();
    }

    /// <summary>Lưu tệp tải lên. Kiểm tra dung lượng và phần mở rộng.</summary>
    public async Task<Attachment> UploadAsync(Stream content, string fileName, long size, int? taskId, int? projectId, AttachmentKind? kind = null)
    {
        var errors = new List<string>();
        var safeName = Path.GetFileName(fileName ?? "").Trim();
        if (safeName.Length == 0) errors.Add("Tên tệp không hợp lệ.");
        var ext = Path.GetExtension(safeName);
        if (!AllowedExtensions.Contains(ext)) errors.Add($"Không hỗ trợ loại tệp \"{ext}\".");
        if (size > MaxBytes) errors.Add($"Tệp vượt quá {MaxBytes / 1024 / 1024} MB.");
        if (taskId == null && projectId == null) errors.Add("Tài liệu phải gắn với một công việc hoặc dự án.");
        if (errors.Count > 0) throw new ValidationException(errors);

        var now = _clock.Now;
        var rel = Path.Combine(now.ToString("yyyy"), now.ToString("MM"), $"{Guid.NewGuid():N}{ext.ToLowerInvariant()}");
        var full = Path.Combine(FilesDir, rel);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        long written;
        await using (var fs = new FileStream(full, FileMode.CreateNew, FileAccess.Write))
        {
            await content.CopyToAsync(fs);
            written = fs.Length;
        }
        if (written > MaxBytes)
        {
            File.Delete(full);
            throw new ValidationException(new[] { $"Tệp vượt quá {MaxBytes / 1024 / 1024} MB." });
        }

        await using var db = await _factory.CreateDbContextAsync();
        var a = new Attachment
        {
            Name = safeName, StoredFile = rel, SizeBytes = written, TaskId = taskId, ProjectId = projectId,
            Kind = kind ?? GuessKind(safeName), CreatedAt = now
        };
        db.Attachments.Add(a);
        if (taskId.HasValue)
            db.TaskHistories.Add(new TaskHistory { TaskId = taskId.Value, At = now, Action = "Hồ sơ", Detail = $"Thêm tài liệu: {safeName}" });
        await db.SaveChangesAsync();
        return a;
    }

    /// <summary>Thêm đường dẫn mạng nội bộ, thư mục hồ sơ hoặc liên kết web.</summary>
    public async Task<Attachment> AddLinkAsync(string name, string link, AttachmentKind kind, int? taskId, int? projectId)
    {
        if (string.IsNullOrWhiteSpace(link)) throw new ValidationException(new[] { "Vui lòng nhập đường dẫn." });
        if (taskId == null && projectId == null) throw new ValidationException(new[] { "Tài liệu phải gắn với một công việc hoặc dự án." });
        await using var db = await _factory.CreateDbContextAsync();
        var now = _clock.Now;
        var a = new Attachment
        {
            Name = string.IsNullOrWhiteSpace(name) ? link.Trim() : name.Trim(), Link = link.Trim(), Kind = kind,
            TaskId = taskId, ProjectId = projectId, CreatedAt = now
        };
        db.Attachments.Add(a);
        if (taskId.HasValue)
            db.TaskHistories.Add(new TaskHistory { TaskId = taskId.Value, At = now, Action = "Hồ sơ", Detail = $"Thêm đường dẫn: {a.Name}" });
        await db.SaveChangesAsync();
        return a;
    }

    public async Task UpdateMetaAsync(int id, AttachmentKind kind, DocumentStatus status, string? documentNumber)
    {
        await using var db = await _factory.CreateDbContextAsync();
        var a = await db.Attachments.FirstOrDefaultAsync(x => x.Id == id) ?? throw new InvalidOperationException("Không tìm thấy tài liệu.");
        a.Kind = kind;
        a.DocumentStatus = status;
        a.DocumentNumber = string.IsNullOrWhiteSpace(documentNumber) ? null : documentNumber.Trim();
        await db.SaveChangesAsync();
    }

    /// <summary>Xóa bản ghi tài liệu và tệp đã tải lên máy chủ (không đụng tới tệp gốc trên máy người dùng).</summary>
    public async Task DeleteAsync(int id)
    {
        await using var db = await _factory.CreateDbContextAsync();
        var a = await db.Attachments.FirstOrDefaultAsync(x => x.Id == id);
        if (a == null) return;
        db.Attachments.Remove(a);
        if (a.TaskId.HasValue)
            db.TaskHistories.Add(new TaskHistory { TaskId = a.TaskId.Value, At = _clock.Now, Action = "Hồ sơ", Detail = $"Gỡ tài liệu: {a.Name}" });
        await db.SaveChangesAsync();
        DeleteStored(a.StoredFile);
    }

    private void DeleteStored(string? rel)
    {
        if (string.IsNullOrEmpty(rel)) return;
        try
        {
            var full = ResolveStoredPath(rel);
            if (full != null && File.Exists(full)) File.Delete(full);
        }
        catch (Exception ex) { AppLog.Error(ex, "Xóa tệp đính kèm"); }
    }

    /// <summary>Đường dẫn tuyệt đối của tệp đã lưu; null nếu đường dẫn nằm ngoài thư mục tệp (chặn truy cập trái phép).</summary>
    public string? ResolveStoredPath(string rel)
    {
        // Coi cả "\" và "/" là dấu phân cách thư mục (dữ liệu tạo trên Windows vẫn dùng được trên máy chủ Linux)
        rel = rel.Replace('\\', '/').Replace('/', Path.DirectorySeparatorChar);
        var root = Path.GetFullPath(FilesDir) + Path.DirectorySeparatorChar;
        var full = Path.GetFullPath(Path.Combine(FilesDir, rel));
        return full.StartsWith(root, StringComparison.OrdinalIgnoreCase) ? full : null;
    }

    public static string ContentType(string fileName) => Path.GetExtension(fileName).ToLowerInvariant() switch
    {
        ".pdf" => "application/pdf",
        ".doc" => "application/msword",
        ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        ".xls" => "application/vnd.ms-excel",
        ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        ".ppt" => "application/vnd.ms-powerpoint",
        ".pptx" => "application/vnd.openxmlformats-officedocument.presentationml.presentation",
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".gif" => "image/gif",
        ".txt" or ".csv" => "text/plain",
        ".zip" => "application/zip",
        _ => "application/octet-stream"
    };

    public static string SizeText(long bytes) => bytes <= 0 ? "" :
        bytes < 1024 * 1024 ? $"{bytes / 1024.0:0.#} KB" : $"{bytes / 1024.0 / 1024.0:0.##} MB";
}
