using System.Security.Cryptography;
using EEMC.WM.Core.Domain;
using EEMC.WM.Core.Text;
using EEMC.WM.Core.Time;
using EEMC.WM.Data.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace EEMC.WM.Data.Services;

/// <summary>Nơi đặt tài liệu: đúng một trong thư mục, công việc, dự án, văn bản (quyết định).</summary>
public readonly record struct DocTarget(int? FolderId = null, int? TaskId = null, int? ProjectId = null, int? LegalDocumentId = null)
{
    public static DocTarget Folder(int id) => new(FolderId: id);
    public static DocTarget Task(int id) => new(TaskId: id);
    public static DocTarget Project(int id) => new(ProjectId: id);
    public static DocTarget Legal(int id) => new(LegalDocumentId: id);

    public int Count => (FolderId.HasValue ? 1 : 0) + (TaskId.HasValue ? 1 : 0) + (ProjectId.HasValue ? 1 : 0) + (LegalDocumentId.HasValue ? 1 : 0);
}

/// <summary>Một vị trí của tài liệu kèm tên hiển thị và đường dẫn trang tương ứng.</summary>
public sealed record PlaceInfo(int LinkId, DocTarget Target, string Label, string Href);

public sealed class AttachmentRow
{
    public required Attachment Attachment { get; init; }
    public List<PlaceInfo> Places { get; init; } = new();
}

/// <summary>Kết quả tải lên: tài liệu (mới hoặc đã có) và cho biết nội dung có trùng tài liệu sẵn có không.</summary>
public sealed record UploadResult(Attachment Attachment, bool Duplicate);

/// <summary>
/// Hồ sơ, tài liệu: lưu tệp tải lên trong thư mục dữ liệu (files\yyyy\MM\...) hoặc chỉ lưu đường dẫn/liên kết.
/// Tệp gốc không bao giờ bị sửa; mỗi lần tải bản mới là một phiên bản, bản cũ được giữ. Tệp trùng nội dung
/// (cùng SHA-256) không lưu lần hai mà gắn thêm vị trí cho tài liệu sẵn có.
/// </summary>
public sealed class AttachmentService
{
    public const long MaxBytes = 50L * 1024 * 1024;
    /// <summary>Chỉ đọc nội dung tệp nhỏ hơn mức này (NAS ít bộ nhớ).</summary>
    public const long MaxExtractBytes = 20L * 1024 * 1024;
    private const int MaxExtractChars = 300_000;

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

    /// <summary>Có thay đổi hồ sơ (để các trang đang mở tải lại).</summary>
    public event EventHandler? Changed;
    internal void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);

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

    // ---------------------------------------------------------------- Đọc

    /// <summary>Danh sách nhẹ (không kèm nội dung đã trích) để hiển thị bảng.</summary>
    internal static IQueryable<Attachment> Light(IQueryable<Attachment> q) => q.Select(a => new Attachment
    {
        Id = a.Id, Name = a.Name, Kind = a.Kind, Link = a.Link, StoredFile = a.StoredFile, SizeBytes = a.SizeBytes, Sha256 = a.Sha256,
        CurrentVersion = a.CurrentVersion, DocumentNumber = a.DocumentNumber, DocumentStatus = a.DocumentStatus, UploadedBy = a.UploadedBy,
        IsDeleted = a.IsDeleted, DeletedAt = a.DeletedAt, CreatedAt = a.CreatedAt, UpdatedAt = a.UpdatedAt
    });

    /// <summary>Tài liệu của công việc: gắn trực tiếp hoặc nằm trong thư mục của công việc.</summary>
    public Task<List<Attachment>> GetForTaskAsync(int taskId) => GetForOwnerAsync(l => l.TaskId == taskId, f => f.TaskId == taskId);

    /// <summary>Tài liệu của dự án: gắn trực tiếp hoặc nằm trong thư mục của dự án.</summary>
    public Task<List<Attachment>> GetForProjectAsync(int projectId) => GetForOwnerAsync(l => l.ProjectId == projectId, f => f.ProjectId == projectId);

    public Task<List<Attachment>> GetForLegalAsync(int legalId) => GetForOwnerAsync(l => l.LegalDocumentId == legalId, f => f.LegalDocumentId == legalId);

    private async Task<List<Attachment>> GetForOwnerAsync(System.Linq.Expressions.Expression<Func<AttachmentLink, bool>> direct,
        System.Linq.Expressions.Expression<Func<DocFolder, bool>> ownedFolder)
    {
        await using var db = await _factory.CreateDbContextAsync();
        var folderIds = db.DocFolders.Where(ownedFolder).Where(f => !f.IsDeleted).Select(f => f.Id);
        var ids = db.AttachmentLinks.Where(direct).Select(l => l.AttachmentId)
            .Union(db.AttachmentLinks.Where(l => l.FolderId != null && folderIds.Contains(l.FolderId.Value)).Select(l => l.AttachmentId));
        return await Light(db.Attachments.AsNoTracking().Where(a => ids.Contains(a.Id) && !a.IsDeleted))
            .OrderByDescending(a => a.UpdatedAt).ToListAsync();
    }

    /// <summary>Tài liệu của dự án/công việc/quyết định kèm danh sách vị trí (để biết tài liệu gắn trực tiếp hay nằm trong thư mục).</summary>
    public async Task<List<AttachmentRow>> GetRowsForAsync(DocOwner owner)
    {
        var list = owner.TaskId is int t ? await GetForTaskAsync(t)
            : owner.ProjectId is int p ? await GetForProjectAsync(p)
            : owner.LegalDocumentId is int l ? await GetForLegalAsync(l) : new();
        await using var db = await _factory.CreateDbContextAsync();
        return await BuildRowsAsync(db, list);
    }

    public async Task<Attachment?> GetAsync(int id)
    {
        await using var db = await _factory.CreateDbContextAsync();
        return await db.Attachments.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id);
    }

    public async Task<List<AttachmentVersion>> GetVersionsAsync(int id)
    {
        await using var db = await _factory.CreateDbContextAsync();
        return await db.AttachmentVersions.AsNoTracking().Where(v => v.AttachmentId == id).OrderByDescending(v => v.VersionNo).ToListAsync();
    }

    public async Task<AttachmentVersion?> GetVersionAsync(int id, int versionNo)
    {
        await using var db = await _factory.CreateDbContextAsync();
        return await db.AttachmentVersions.AsNoTracking().FirstOrDefaultAsync(v => v.AttachmentId == id && v.VersionNo == versionNo);
    }

    /// <summary>Các vị trí của tài liệu.</summary>
    public async Task<List<PlaceInfo>> GetPlacesAsync(int id)
    {
        await using var db = await _factory.CreateDbContextAsync();
        return (await BuildRowsAsync(db, new List<Attachment> { new() { Id = id } })).Single().Places;
    }

    /// <summary>Tìm theo tên, số văn bản, nội dung tài liệu (không phân biệt dấu) và theo công việc/dự án/văn bản đang chứa tài liệu.</summary>
    public async Task<List<AttachmentRow>> SearchAsync(string? text, AttachmentKind? kind, DocumentStatus? status, int take = 300)
    {
        await using var db = await _factory.CreateDbContextAsync();
        var q = db.Attachments.AsNoTracking().Where(a => !a.IsDeleted);
        if (kind.HasValue) q = q.Where(a => a.Kind == kind.Value);
        if (status.HasValue) q = q.Where(a => a.DocumentStatus == status.Value);
        var terms = TextNormalizer.Normalize(text).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (terms.Length > 0)
        {
            // Khớp nội dung tài liệu, hoặc nằm ở công việc/dự án/văn bản có tên khớp
            var byText = db.Attachments.AsQueryable();
            foreach (var t in terms) byText = byText.Where(a => a.SearchText.Contains(t));
            var tasks = db.Tasks.Where(x => !x.IsDeleted);
            foreach (var t in terms) tasks = tasks.Where(x => x.SearchText.Contains(t));
            var taskIds = tasks.Select(x => x.Id);
            var projects = await db.Projects.Select(p => new { p.Id, p.Code, p.Name }).ToListAsync();
            var projIds = projects.Where(p => terms.All(TextNormalizer.Normalize(p.Code + " " + p.Name).Contains)).Select(p => p.Id).ToList();
            var legal = db.LegalDocuments.AsQueryable();
            foreach (var t in terms) legal = legal.Where(x => x.SearchText.Contains(t));
            var legalIds = legal.Select(x => x.Id);
            var byOwner = db.AttachmentLinks.Where(l => (l.TaskId != null && taskIds.Contains(l.TaskId.Value))
                    || (l.ProjectId != null && projIds.Contains(l.ProjectId.Value))
                    || (l.LegalDocumentId != null && legalIds.Contains(l.LegalDocumentId.Value)))
                .Select(l => l.AttachmentId);
            var byTextIds = byText.Select(x => x.Id);
            q = q.Where(a => byTextIds.Contains(a.Id) || byOwner.Contains(a.Id));
        }
        var rows = await Light(q).OrderByDescending(a => a.UpdatedAt).Take(take).ToListAsync();
        return await BuildRowsAsync(db, rows);
    }

    /// <summary>Ghép tên hiển thị các vị trí cho danh sách tài liệu (bỏ vị trí trong thư mục đã xóa).</summary>
    internal static async Task<List<AttachmentRow>> BuildRowsAsync(AppDbContext db, List<Attachment> items)
    {
        var ids = items.Select(a => a.Id).ToList();
        var links = await db.AttachmentLinks.AsNoTracking().Where(l => ids.Contains(l.AttachmentId)).ToListAsync();
        var taskIds = links.Where(l => l.TaskId != null).Select(l => l.TaskId!.Value).Distinct().ToList();
        var projIds = links.Where(l => l.ProjectId != null).Select(l => l.ProjectId!.Value).Distinct().ToList();
        var legalIds = links.Where(l => l.LegalDocumentId != null).Select(l => l.LegalDocumentId!.Value).Distinct().ToList();
        var folderIds = links.Where(l => l.FolderId != null).Select(l => l.FolderId!.Value).Distinct().ToList();
        var tasks = await db.Tasks.AsNoTracking().Where(t => taskIds.Contains(t.Id)).Select(t => new { t.Id, t.Code, t.Title }).ToDictionaryAsync(t => t.Id);
        var projects = await db.Projects.AsNoTracking().Where(p => projIds.Contains(p.Id)).Select(p => new { p.Id, p.Code, p.Name }).ToDictionaryAsync(p => p.Id);
        var legal = await db.LegalDocuments.AsNoTracking().Where(d => legalIds.Contains(d.Id)).Select(d => new { d.Id, d.Number, d.Title }).ToDictionaryAsync(d => d.Id);
        var folderPaths = await DocumentService.FolderPathsAsync(db, folderIds);
        var result = new List<AttachmentRow>();
        foreach (var a in items)
        {
            var places = new List<PlaceInfo>();
            foreach (var l in links.Where(l => l.AttachmentId == a.Id))
            {
                var target = new DocTarget(l.FolderId, l.TaskId, l.ProjectId, l.LegalDocumentId);
                if (l.FolderId is int fid && folderPaths.TryGetValue(fid, out var path))
                    places.Add(new PlaceInfo(l.Id, target, "Thư mục: " + path, $"/documents?c=f:{fid}"));
                else if (l.TaskId is int tid && tasks.TryGetValue(tid, out var t))
                    places.Add(new PlaceInfo(l.Id, target, $"{t.Code} – {t.Title}", $"/tasks/{tid}"));
                else if (l.ProjectId is int pid && projects.TryGetValue(pid, out var p))
                    places.Add(new PlaceInfo(l.Id, target, $"{p.Code} – {p.Name}", $"/projects/{pid}"));
                else if (l.LegalDocumentId is int lid && legal.TryGetValue(lid, out var d))
                    places.Add(new PlaceInfo(l.Id, target, string.IsNullOrEmpty(d.Number) ? d.Title : $"{d.Number} – {d.Title}", $"/legal/{lid}"));
            }
            result.Add(new AttachmentRow { Attachment = a, Places = places });
        }
        return result;
    }

    // ---------------------------------------------------------------- Ghi

    internal static void ValidateTarget(DocTarget target)
    {
        if (target.Count != 1) throw new ValidationException(new[] { "Tài liệu phải đặt vào đúng một thư mục, công việc, dự án hoặc văn bản." });
    }

    internal static async Task CheckTargetExistsAsync(AppDbContext db, DocTarget t)
    {
        bool ok = t.FolderId is int f ? await db.DocFolders.AnyAsync(x => x.Id == f && !x.IsDeleted)
            : t.TaskId is int tk ? await db.Tasks.AnyAsync(x => x.Id == tk && !x.IsDeleted)
            : t.ProjectId is int p ? await db.Projects.AnyAsync(x => x.Id == p)
            : t.LegalDocumentId is int l && await db.LegalDocuments.AnyAsync(x => x.Id == l);
        if (!ok) throw new InvalidOperationException("Không tìm thấy nơi đặt tài liệu (có thể đã bị xóa).");
    }

    internal static Task<bool> HasPlacementAsync(AppDbContext db, int attachmentId, DocTarget t) =>
        db.AttachmentLinks.AnyAsync(l => l.AttachmentId == attachmentId && l.FolderId == t.FolderId && l.TaskId == t.TaskId
                                         && l.ProjectId == t.ProjectId && l.LegalDocumentId == t.LegalDocumentId);

    /// <summary>Lưu tệp tải lên vào nơi chỉ định. Tệp trùng nội dung với tài liệu sẵn có: không lưu lần hai, chỉ gắn thêm vị trí.</summary>
    public async Task<UploadResult> UploadAsync(Stream content, string fileName, long size, DocTarget target, AttachmentKind? kind = null)
    {
        Access.Require(UserRole.Editor);
        ValidateTarget(target);
        var stored = await StoreAsync(content, fileName, size);
        await using var db = await _factory.CreateDbContextAsync();
        try { await CheckTargetExistsAsync(db, target); }
        catch { DeleteStored(stored.Rel); throw; }
        var now = _clock.Now;

        var existing = await db.Attachments.FirstOrDefaultAsync(a => a.Sha256 == stored.Sha256 && a.SizeBytes == stored.Size && !a.IsDeleted);
        if (existing != null)
        {
            DeleteStored(stored.Rel);
            if (!await HasPlacementAsync(db, existing.Id, target))
            {
                AddPlacement(db, existing.Id, target, now);
                AddHistory(db, target, now, $"Gắn tài liệu đã có: {existing.Name}");
                await db.SaveChangesAsync();
            }
            RaiseChanged();
            return new UploadResult(existing, true);
        }

        var a = new Attachment
        {
            Name = stored.Name, StoredFile = stored.Rel, SizeBytes = stored.Size, Sha256 = stored.Sha256, CurrentVersion = 1,
            Kind = kind ?? GuessKind(stored.Name), UploadedBy = Access.Current?.Name, CreatedAt = now, UpdatedAt = now
        };
        ApplyText(a, stored.Text);
        db.Attachments.Add(a);
        await db.SaveChangesAsync();
        db.AttachmentVersions.Add(new AttachmentVersion
        {
            AttachmentId = a.Id, VersionNo = 1, StoredFile = stored.Rel, FileName = stored.Name, SizeBytes = stored.Size,
            Sha256 = stored.Sha256, UploadedBy = a.UploadedBy, UploadedAt = now
        });
        AddPlacement(db, a.Id, target, now);
        AddHistory(db, target, now, $"Thêm tài liệu: {stored.Name}");
        await db.SaveChangesAsync();
        RaiseChanged();
        return new UploadResult(a, false);
    }

    /// <summary>Tải lên phiên bản mới của tài liệu; phiên bản cũ vẫn được giữ.</summary>
    public async Task<AttachmentVersion> UploadVersionAsync(int id, Stream content, string fileName, long size, string? note)
    {
        Access.Require(UserRole.Editor);
        await using var db = await _factory.CreateDbContextAsync();
        var a = await db.Attachments.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted) ?? throw new InvalidOperationException("Không tìm thấy tài liệu.");
        if (a.StoredFile == null) throw new ValidationException(new[] { "Tài liệu dạng đường dẫn/liên kết không có phiên bản tệp." });
        var stored = await StoreAsync(content, fileName, size);
        if (stored.Sha256 == a.Sha256)
        {
            DeleteStored(stored.Rel);
            throw new ValidationException(new[] { "Tệp mới giống hệt phiên bản hiện hành." });
        }
        var now = _clock.Now;
        var no = (await db.AttachmentVersions.Where(v => v.AttachmentId == id).Select(v => (int?)v.VersionNo).MaxAsync() ?? 0) + 1;
        var v = new AttachmentVersion
        {
            AttachmentId = id, VersionNo = no, StoredFile = stored.Rel, FileName = stored.Name, SizeBytes = stored.Size, Sha256 = stored.Sha256,
            Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim(), UploadedBy = Access.Current?.Name, UploadedAt = now
        };
        db.AttachmentVersions.Add(v);
        a.StoredFile = stored.Rel;
        a.SizeBytes = stored.Size;
        a.Sha256 = stored.Sha256;
        a.CurrentVersion = no;
        a.UpdatedAt = now;
        ApplyText(a, stored.Text);
        await db.SaveChangesAsync();
        RaiseChanged();
        return v;
    }

    /// <summary>Đưa một phiên bản cũ trở lại làm hiện hành (tạo phiên bản mới dùng lại tệp cũ, không sao chép tệp).</summary>
    public async Task RestoreVersionAsync(int id, int versionNo)
    {
        Access.Require(UserRole.Editor);
        await using var db = await _factory.CreateDbContextAsync();
        var a = await db.Attachments.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted) ?? throw new InvalidOperationException("Không tìm thấy tài liệu.");
        var old = await db.AttachmentVersions.AsNoTracking().FirstOrDefaultAsync(v => v.AttachmentId == id && v.VersionNo == versionNo)
                  ?? throw new InvalidOperationException("Không tìm thấy phiên bản.");
        if (versionNo == a.CurrentVersion) return;
        var now = _clock.Now;
        var no = await db.AttachmentVersions.Where(v => v.AttachmentId == id).MaxAsync(v => v.VersionNo) + 1;
        db.AttachmentVersions.Add(new AttachmentVersion
        {
            AttachmentId = id, VersionNo = no, StoredFile = old.StoredFile, FileName = old.FileName, SizeBytes = old.SizeBytes, Sha256 = old.Sha256,
            Note = $"Khôi phục từ phiên bản {versionNo}", UploadedBy = Access.Current?.Name, UploadedAt = now
        });
        a.StoredFile = old.StoredFile;
        a.SizeBytes = old.SizeBytes;
        a.Sha256 = old.Sha256;
        a.CurrentVersion = no;
        a.UpdatedAt = now;
        await db.SaveChangesAsync();
        await IndexAsync(db, a);
        RaiseChanged();
    }

    /// <summary>Thêm đường dẫn mạng nội bộ, thư mục hồ sơ hoặc liên kết web.</summary>
    public async Task<Attachment> AddLinkAsync(string name, string link, AttachmentKind kind, DocTarget target)
    {
        Access.Require(UserRole.Editor);
        if (string.IsNullOrWhiteSpace(link)) throw new ValidationException(new[] { "Vui lòng nhập đường dẫn." });
        ValidateTarget(target);
        await using var db = await _factory.CreateDbContextAsync();
        await CheckTargetExistsAsync(db, target);
        var now = _clock.Now;
        var a = new Attachment
        {
            Name = string.IsNullOrWhiteSpace(name) ? link.Trim() : name.Trim(), Link = link.Trim(), Kind = kind,
            UploadedBy = Access.Current?.Name, CreatedAt = now, UpdatedAt = now
        };
        ApplyText(a, null);
        db.Attachments.Add(a);
        await db.SaveChangesAsync();
        AddPlacement(db, a.Id, target, now);
        AddHistory(db, target, now, $"Thêm đường dẫn: {a.Name}");
        await db.SaveChangesAsync();
        RaiseChanged();
        return a;
    }

    public async Task UpdateMetaAsync(int id, AttachmentKind kind, DocumentStatus status, string? documentNumber)
    {
        Access.Require(UserRole.Editor);
        await using var db = await _factory.CreateDbContextAsync();
        var a = await db.Attachments.FirstOrDefaultAsync(x => x.Id == id) ?? throw new InvalidOperationException("Không tìm thấy tài liệu.");
        a.Kind = kind;
        a.DocumentStatus = status;
        a.DocumentNumber = string.IsNullOrWhiteSpace(documentNumber) ? null : documentNumber.Trim();
        a.UpdatedAt = _clock.Now;
        Reindex(a);
        await db.SaveChangesAsync();
        RaiseChanged();
    }

    public async Task RenameAsync(int id, string name)
    {
        Access.Require(UserRole.Editor);
        if (string.IsNullOrWhiteSpace(name)) throw new ValidationException(new[] { "Vui lòng nhập tên tài liệu." });
        await using var db = await _factory.CreateDbContextAsync();
        var a = await db.Attachments.FirstOrDefaultAsync(x => x.Id == id) ?? throw new InvalidOperationException("Không tìm thấy tài liệu.");
        a.Name = name.Trim();
        a.UpdatedAt = _clock.Now;
        Reindex(a);
        await db.SaveChangesAsync();
        RaiseChanged();
    }

    // ---------------------------------------------------------------- Đọc nội dung để tìm kiếm

    /// <summary>Đọc nội dung các tài liệu chưa lập chỉ mục (tài liệu có từ trước bản này). Trả về số tài liệu đã xử lý.</summary>
    public async Task<int> IndexPendingAsync(int batch = 10)
    {
        await using var db = await _factory.CreateDbContextAsync();
        var pending = await db.Attachments.Where(a => a.ExtractedText == null && !a.IsDeleted).OrderBy(a => a.Id).Take(batch).ToListAsync();
        foreach (var a in pending) await IndexAsync(db, a);
        return pending.Count;
    }

    private async Task IndexAsync(AppDbContext db, Attachment a)
    {
        string? text = null;
        if (a.StoredFile != null && TextExtractor.Supported.Contains(Path.GetExtension(a.StoredFile)) && a.SizeBytes <= MaxExtractBytes)
        {
            var full = ResolveStoredPath(a.StoredFile);
            try { if (full != null && File.Exists(full)) text = TextExtractor.Read(full); }
            catch (Exception ex) { AppLog.Error(ex, "Đọc nội dung tài liệu " + a.Name); }
        }
        ApplyText(a, text);
        await db.SaveChangesAsync();
    }

    private static void ApplyText(Attachment a, string? text)
    {
        a.ExtractedText = text == null ? "" : text.Length > MaxExtractChars ? text[..MaxExtractChars] : text;
        Reindex(a);
    }

    private static void Reindex(Attachment a) =>
        a.SearchText = TextNormalizer.Normalize(string.Join(" ", a.Name, a.DocumentNumber, a.Link, a.ExtractedText));

    // ---------------------------------------------------------------- Lưu tệp

    private sealed record Stored(string Rel, string Name, long Size, string Sha256, string? Text);

    private async Task<Stored> StoreAsync(Stream content, string fileName, long size)
    {
        var errors = new List<string>();
        var safeName = Path.GetFileName(fileName ?? "").Trim();
        if (safeName.Length == 0) errors.Add("Tên tệp không hợp lệ.");
        var ext = Path.GetExtension(safeName);
        if (!AllowedExtensions.Contains(ext)) errors.Add($"Không hỗ trợ loại tệp \"{ext}\".");
        if (size > MaxBytes) errors.Add($"Tệp vượt quá {MaxBytes / 1024 / 1024} MB.");
        if (errors.Count > 0) throw new ValidationException(errors);

        var now = _clock.Now;
        var rel = Path.Combine(now.ToString("yyyy"), now.ToString("MM"), $"{Guid.NewGuid():N}{ext.ToLowerInvariant()}");
        var full = Path.Combine(FilesDir, rel);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        long written;
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        await using (var fs = new FileStream(full, FileMode.CreateNew, FileAccess.Write))
        {
            var buffer = new byte[81920];
            int n;
            while ((n = await content.ReadAsync(buffer)) > 0)
            {
                sha.AppendData(buffer, 0, n);
                await fs.WriteAsync(buffer.AsMemory(0, n));
                if (fs.Length > MaxBytes) break;
            }
            written = fs.Length;
        }
        if (written > MaxBytes)
        {
            File.Delete(full);
            throw new ValidationException(new[] { $"Tệp vượt quá {MaxBytes / 1024 / 1024} MB." });
        }
        string? text = null;
        if (TextExtractor.Supported.Contains(ext) && written <= MaxExtractBytes)
        {
            try { text = TextExtractor.Read(full); }
            catch (Exception ex) { AppLog.Error(ex, "Đọc nội dung tài liệu " + safeName); }
        }
        return new Stored(rel, safeName, written, Convert.ToHexString(sha.GetHashAndReset()).ToLowerInvariant(), text);
    }

    internal static void AddPlacement(AppDbContext db, int attachmentId, DocTarget t, DateTime now) =>
        db.AttachmentLinks.Add(new AttachmentLink
        {
            AttachmentId = attachmentId, FolderId = t.FolderId, TaskId = t.TaskId, ProjectId = t.ProjectId, LegalDocumentId = t.LegalDocumentId, CreatedAt = now
        });

    internal static void AddHistory(AppDbContext db, DocTarget t, DateTime now, string detail)
    {
        if (t.TaskId is int taskId) db.TaskHistories.Add(new TaskHistory { TaskId = taskId, At = now, Action = "Hồ sơ", Detail = detail });
    }

    /// <summary>Xóa tệp trên đĩa nếu không còn phiên bản/tài liệu nào dùng.</summary>
    internal async Task DeleteStoredIfUnusedAsync(AppDbContext db, IEnumerable<string> rels)
    {
        foreach (var rel in rels.Distinct())
            if (!await db.AttachmentVersions.AnyAsync(v => v.StoredFile == rel) && !await db.Attachments.AnyAsync(a => a.StoredFile == rel))
                DeleteStored(rel);
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

    public static bool IsImage(string name) => Path.GetExtension(name).ToLowerInvariant() is ".png" or ".jpg" or ".jpeg" or ".gif";

    public static string SizeText(long bytes) => bytes <= 0 ? "" :
        bytes < 1024 * 1024 ? $"{bytes / 1024.0:0.#} KB" : $"{bytes / 1024.0 / 1024.0:0.##} MB";
}
