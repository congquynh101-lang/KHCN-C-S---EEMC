using EEMC.WM.Core.Domain;
using EEMC.WM.Core.Text;
using EEMC.WM.Core.Time;
using EEMC.WM.Data.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace EEMC.WM.Data.Services;

/// <summary>Chủ sở hữu của thư mục: một dự án, công việc, quyết định, hoặc không có (thư mục chung).</summary>
public readonly record struct DocOwner(int? ProjectId = null, int? TaskId = null, int? LegalDocumentId = null)
{
    public static readonly DocOwner Common = new();
    public bool IsCommon => ProjectId == null && TaskId == null && LegalDocumentId == null;
    public static DocOwner Of(DocFolder f) => new(f.ProjectId, f.TaskId, f.LegalDocumentId);
    /// <summary>Nơi đặt tài liệu tương ứng thư mục gốc của chủ sở hữu (không có với thư mục chung).</summary>
    public DocTarget? AsTarget => ProjectId is int p ? DocTarget.Project(p) : TaskId is int t ? DocTarget.Task(t)
        : LegalDocumentId is int l ? DocTarget.Legal(l) : null;
}

public sealed record FolderInfo(DocFolder Folder, int SubFolders, int Files);

/// <summary>Nội dung một nơi chứa: thư mục con và tài liệu đặt trực tiếp tại đó.</summary>
public sealed class ContainerContent
{
    public List<FolderInfo> Folders { get; init; } = new();
    public List<AttachmentRow> Files { get; init; } = new();
}

public sealed class TrashContent
{
    public List<DocFolder> Folders { get; init; } = new();
    public List<AttachmentRow> Files { get; init; } = new();
}

/// <summary>
/// Cây thư mục hồ sơ theo dự án / công việc / quyết định / thư mục chung; tạo, đổi tên, di chuyển, xóa (vào thùng rác)
/// thư mục; đặt một tài liệu vào nhiều nơi mà không nhân bản tệp. Xóa vĩnh viễn chỉ dành cho quản trị.
/// </summary>
public sealed class DocumentService
{
    public const int MaxFolderDepth = 8;

    private readonly IDbContextFactory<AppDbContext> _factory;
    private readonly AttachmentService _attachments;
    private readonly IClock _clock;

    public DocumentService(IDbContextFactory<AppDbContext> factory, AttachmentService attachments, IClock clock)
    {
        _factory = factory;
        _attachments = attachments;
        _clock = clock;
    }

    // ---------------------------------------------------------------- Cây (tải từng cấp)

    public sealed record RootCounts(int Projects, int Decisions, int LooseTasks, int CommonFolders, int Trash);

    public async Task<RootCounts> GetRootCountsAsync()
    {
        await using var db = await _factory.CreateDbContextAsync();
        var looseTasks = (await TaskIdsWithDocsAsync(db)).Count(t => t.ProjectId == null);
        return new RootCounts(
            await db.Projects.CountAsync(),
            await db.LegalDocuments.CountAsync(d => d.DocType == LegalDocType.Decision),
            looseTasks,
            await db.DocFolders.CountAsync(f => !f.IsDeleted && f.ParentId == null && f.ProjectId == null && f.TaskId == null && f.LegalDocumentId == null),
            await TrashCountAsync(db));
    }

    /// <summary>Dự án kèm số thư mục gốc, số tài liệu đặt trực tiếp, số công việc có hồ sơ.</summary>
    public async Task<List<(Project Project, int Folders, int Files, int TasksWithDocs)>> GetProjectsAsync()
    {
        await using var db = await _factory.CreateDbContextAsync();
        var projects = await db.Projects.AsNoTracking().OrderBy(p => p.IsArchived).ThenBy(p => p.Code).ToListAsync();
        var folders = await TopFolderCountsAsync(db, f => f.ProjectId != null, f => f.ProjectId!.Value);
        var files = await DirectFileCountsAsync(db, l => l.ProjectId != null, l => l.ProjectId!.Value);
        var tasks = (await TaskIdsWithDocsAsync(db)).Where(t => t.ProjectId != null).GroupBy(t => t.ProjectId!.Value).ToDictionary(g => g.Key, g => g.Count());
        return projects.Select(p => (p, folders.GetValueOrDefault(p.Id), files.GetValueOrDefault(p.Id), tasks.GetValueOrDefault(p.Id))).ToList();
    }

    /// <summary>Công việc có hồ sơ của một dự án (null = công việc không thuộc dự án).</summary>
    public async Task<List<(WorkTask Task, int Folders, int Files)>> GetTasksWithDocsAsync(int? projectId)
    {
        await using var db = await _factory.CreateDbContextAsync();
        var ids = (await TaskIdsWithDocsAsync(db)).Where(t => t.ProjectId == projectId).Select(t => t.Id).ToList();
        var tasks = await db.Tasks.AsNoTracking().Where(t => ids.Contains(t.Id)).OrderBy(t => t.Code).ToListAsync();
        var folders = await TopFolderCountsAsync(db, f => f.TaskId != null, f => f.TaskId!.Value);
        var files = await DirectFileCountsAsync(db, l => l.TaskId != null, l => l.TaskId!.Value);
        return tasks.Select(t => (t, folders.GetValueOrDefault(t.Id), files.GetValueOrDefault(t.Id))).ToList();
    }

    /// <summary>Văn bản loại Quyết định, dùng để gom hồ sơ theo quyết định.</summary>
    public async Task<List<(LegalDocument Decision, int Folders, int Files)>> GetDecisionsAsync()
    {
        await using var db = await _factory.CreateDbContextAsync();
        var docs = await db.LegalDocuments.AsNoTracking().Where(d => d.DocType == LegalDocType.Decision)
            .OrderByDescending(d => d.IssuedDate ?? d.CreatedAt)
            .Select(d => new LegalDocument { Id = d.Id, Number = d.Number, Title = d.Title, DocType = d.DocType, Effect = d.Effect, IssuedDate = d.IssuedDate })
            .ToListAsync();
        var folders = await TopFolderCountsAsync(db, f => f.LegalDocumentId != null, f => f.LegalDocumentId!.Value);
        var files = await DirectFileCountsAsync(db, l => l.LegalDocumentId != null, l => l.LegalDocumentId!.Value);
        return docs.Select(d => (d, folders.GetValueOrDefault(d.Id), files.GetValueOrDefault(d.Id))).ToList();
    }

    /// <summary>Thư mục con của <paramref name="parentId"/>, hoặc thư mục gốc của chủ sở hữu khi parentId null.</summary>
    public async Task<List<FolderInfo>> GetFoldersAsync(DocOwner owner, int? parentId)
    {
        await using var db = await _factory.CreateDbContextAsync();
        return await FolderInfosAsync(db, owner, parentId);
    }

    /// <summary>Nội dung thư mục (folderId) hoặc thư mục gốc của chủ sở hữu (folderId null).</summary>
    public async Task<ContainerContent> GetContentAsync(DocOwner owner, int? folderId)
    {
        await using var db = await _factory.CreateDbContextAsync();
        var folders = await FolderInfosAsync(db, owner, folderId);
        IQueryable<AttachmentLink> links;
        if (folderId is int fid) links = db.AttachmentLinks.Where(l => l.FolderId == fid);
        else if (owner.AsTarget is DocTarget t)
            links = db.AttachmentLinks.Where(l => l.FolderId == null && l.TaskId == t.TaskId && l.ProjectId == t.ProjectId && l.LegalDocumentId == t.LegalDocumentId);
        else links = db.AttachmentLinks.Where(l => false);
        var ids = links.Select(l => l.AttachmentId);
        var files = await AttachmentService.Light(db.Attachments.AsNoTracking().Where(a => ids.Contains(a.Id) && !a.IsDeleted))
            .OrderBy(a => a.Name).ToListAsync();
        return new ContainerContent { Folders = folders, Files = await AttachmentService.BuildRowsAsync(db, files) };
    }

    /// <summary>Tên hiển thị của chủ sở hữu thư mục.</summary>
    public async Task<string> GetOwnerLabelAsync(DocOwner o)
    {
        await using var db = await _factory.CreateDbContextAsync();
        if (o.ProjectId is int p) return await db.Projects.Where(x => x.Id == p).Select(x => x.Code + " – " + x.Name).FirstOrDefaultAsync() ?? "(dự án đã xóa)";
        if (o.TaskId is int t) return await db.Tasks.Where(x => x.Id == t).Select(x => x.Code + " – " + x.Title).FirstOrDefaultAsync() ?? "(công việc đã xóa)";
        if (o.LegalDocumentId is int l)
        {
            var d = await db.LegalDocuments.Where(x => x.Id == l).Select(x => new { x.Number, x.Title }).FirstOrDefaultAsync();
            return d == null ? "(quyết định đã xóa)" : string.IsNullOrEmpty(d.Number) ? d.Title : $"{d.Number} – {d.Title}";
        }
        return "Thư mục chung";
    }

    public async Task<DocFolder?> GetFolderAsync(int id)
    {
        await using var db = await _factory.CreateDbContextAsync();
        return await db.DocFolders.AsNoTracking().FirstOrDefaultAsync(f => f.Id == id);
    }

    /// <summary>Chuỗi thư mục từ gốc đến thư mục <paramref name="id"/>.</summary>
    public async Task<List<DocFolder>> GetFolderPathAsync(int id)
    {
        await using var db = await _factory.CreateDbContextAsync();
        var all = await db.DocFolders.AsNoTracking().ToDictionaryAsync(f => f.Id);
        var path = new List<DocFolder>();
        var seen = new HashSet<int>();
        for (int? cur = id; cur is int c && all.TryGetValue(c, out var f) && seen.Add(c); cur = f.ParentId) path.Insert(0, f);
        return path;
    }

    /// <summary>"Thư mục A › Thư mục B" cho từng thư mục chưa xóa.</summary>
    internal static async Task<Dictionary<int, string>> FolderPathsAsync(AppDbContext db, List<int> ids)
    {
        if (ids.Count == 0) return new();
        var all = await db.DocFolders.AsNoTracking().ToDictionaryAsync(f => f.Id);
        var result = new Dictionary<int, string>();
        foreach (var id in ids)
        {
            if (!all.TryGetValue(id, out var f) || f.IsDeleted) continue;
            var names = new List<string>();
            var seen = new HashSet<int>();
            for (DocFolder? cur = f; cur != null && seen.Add(cur.Id); cur = cur.ParentId is int p && all.TryGetValue(p, out var pf) ? pf : null)
                names.Insert(0, cur.Name);
            result[id] = string.Join(" › ", names);
        }
        return result;
    }

    /// <summary>Thư mục có tên khớp và nơi đặt của tài liệu có tên/nội dung khớp (để mở sẵn nhánh trên cây).</summary>
    public async Task<(List<DocFolder> Folders, List<AttachmentLink> Placements)> SearchAsync(string text, int take = 30)
    {
        var terms = TextNormalizer.Normalize(text).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (terms.Length == 0) return (new(), new());
        await using var db = await _factory.CreateDbContextAsync();
        var folders = (await db.DocFolders.AsNoTracking().Where(f => !f.IsDeleted).ToListAsync())
            .Where(f => terms.All(TextNormalizer.Normalize(f.Name).Contains)).Take(take).ToList();
        var files = db.Attachments.Where(a => !a.IsDeleted);
        foreach (var t in terms) files = files.Where(a => a.SearchText.Contains(t));
        var ids = files.Select(a => a.Id);
        var placements = await db.AttachmentLinks.AsNoTracking().Where(l => ids.Contains(l.AttachmentId)).Take(take).ToListAsync();
        return (folders, placements);
    }

    // ---------------------------------------------------------------- Thư mục

    public async Task<DocFolder> CreateFolderAsync(string name, int? parentId, DocOwner owner)
    {
        Access.Require(UserRole.Editor);
        name = CleanName(name);
        await using var db = await _factory.CreateDbContextAsync();
        if (parentId is int pid)
        {
            var parent = await db.DocFolders.FirstOrDefaultAsync(f => f.Id == pid && !f.IsDeleted) ?? throw new InvalidOperationException("Không tìm thấy thư mục cha.");
            owner = DocOwner.Of(parent);
            var all = await db.DocFolders.AsNoTracking().ToDictionaryAsync(f => f.Id);
            if (Depth(all, pid) + 1 > MaxFolderDepth) throw new ValidationException(new[] { $"Tối đa {MaxFolderDepth} cấp thư mục." });
        }
        else await CheckOwnerAsync(db, owner);
        await CheckNameFreeAsync(db, name, parentId, owner, exceptId: null);
        var f = new DocFolder
        {
            Name = name, ParentId = parentId, ProjectId = owner.ProjectId, TaskId = owner.TaskId, LegalDocumentId = owner.LegalDocumentId,
            CreatedBy = Access.Current?.Name, CreatedAt = _clock.Now
        };
        db.DocFolders.Add(f);
        await db.SaveChangesAsync();
        _attachments.RaiseChanged();
        return f;
    }

    public async Task RenameFolderAsync(int id, string name)
    {
        Access.Require(UserRole.Editor);
        name = CleanName(name);
        await using var db = await _factory.CreateDbContextAsync();
        var f = await db.DocFolders.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted) ?? throw new InvalidOperationException("Không tìm thấy thư mục.");
        await CheckNameFreeAsync(db, name, f.ParentId, DocOwner.Of(f), exceptId: id);
        f.Name = name;
        await db.SaveChangesAsync();
        _attachments.RaiseChanged();
    }

    /// <summary>Chuyển thư mục (kèm thư mục con, tài liệu bên trong) vào thư mục khác hoặc lên gốc của một chủ sở hữu.</summary>
    public async Task MoveFolderAsync(int id, int? newParentId, DocOwner owner)
    {
        Access.Require(UserRole.Editor);
        await using var db = await _factory.CreateDbContextAsync();
        var all = await db.DocFolders.ToDictionaryAsync(f => f.Id);
        if (!all.TryGetValue(id, out var f) || f.IsDeleted) throw new InvalidOperationException("Không tìm thấy thư mục.");
        var subtree = Subtree(all, id);
        if (newParentId is int pid)
        {
            if (!all.TryGetValue(pid, out var parent) || parent.IsDeleted) throw new InvalidOperationException("Không tìm thấy thư mục đích.");
            if (subtree.Contains(pid)) throw new ValidationException(new[] { "Không thể chuyển thư mục vào chính nó hoặc thư mục con của nó." });
            owner = DocOwner.Of(parent);
            if (Depth(all, pid) + Height(all, id) > MaxFolderDepth) throw new ValidationException(new[] { $"Sau khi chuyển sẽ vượt quá {MaxFolderDepth} cấp thư mục." });
        }
        else await CheckOwnerAsync(db, owner);
        await CheckNameFreeAsync(db, f.Name, newParentId, owner, exceptId: id);
        f.ParentId = newParentId;
        foreach (var sid in subtree)
        {
            all[sid].ProjectId = owner.ProjectId;
            all[sid].TaskId = owner.TaskId;
            all[sid].LegalDocumentId = owner.LegalDocumentId;
        }
        await db.SaveChangesAsync();
        _attachments.RaiseChanged();
    }

    /// <summary>Đưa thư mục (và mọi thư mục con) vào thùng rác; tài liệu chỉ nằm trong đó cũng vào thùng rác.</summary>
    public async Task TrashFolderAsync(int id)
    {
        Access.Require(UserRole.Editor);
        await using var db = await _factory.CreateDbContextAsync();
        var all = await db.DocFolders.ToDictionaryAsync(f => f.Id);
        if (!all.TryGetValue(id, out var f) || f.IsDeleted) throw new InvalidOperationException("Không tìm thấy thư mục.");
        var now = _clock.Now;
        var subtree = Subtree(all, id).Where(i => !all[i].IsDeleted).ToList();
        foreach (var sid in subtree)
        {
            all[sid].IsDeleted = true;
            all[sid].DeletedAt = now;
        }
        await db.SaveChangesAsync();
        var affected = await db.AttachmentLinks.Where(l => l.FolderId != null && subtree.Contains(l.FolderId.Value)).Select(l => l.AttachmentId).Distinct().ToListAsync();
        await TrashOrphansAsync(db, affected, now);
        _attachments.RaiseChanged();
    }

    /// <summary>Khôi phục thư mục từ thùng rác (về gốc nếu thư mục cha vẫn đang bị xóa).</summary>
    public async Task RestoreFolderAsync(int id)
    {
        Access.Require(UserRole.Editor);
        await using var db = await _factory.CreateDbContextAsync();
        var all = await db.DocFolders.ToDictionaryAsync(f => f.Id);
        if (!all.TryGetValue(id, out var f) || !f.IsDeleted) throw new InvalidOperationException("Thư mục không nằm trong thùng rác.");
        var stamp = f.DeletedAt;
        if (f.ParentId is int pid && (!all.TryGetValue(pid, out var parent) || parent.IsDeleted)) f.ParentId = null;
        if (await db.DocFolders.AnyAsync(x => x.Id != id && !x.IsDeleted && x.ParentId == f.ParentId && x.Name == f.Name
                && x.ProjectId == f.ProjectId && x.TaskId == f.TaskId && x.LegalDocumentId == f.LegalDocumentId))
            f.Name += " (khôi phục)";
        var subtree = Subtree(all, id).Where(i => all[i].IsDeleted && all[i].DeletedAt == stamp).ToList();
        foreach (var sid in subtree)
        {
            all[sid].IsDeleted = false;
            all[sid].DeletedAt = null;
        }
        var fileIds = await db.AttachmentLinks.Where(l => l.FolderId != null && subtree.Contains(l.FolderId.Value)).Select(l => l.AttachmentId).ToListAsync();
        await db.Attachments.Where(a => fileIds.Contains(a.Id) && a.IsDeleted && a.DeletedAt == stamp)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.IsDeleted, false).SetProperty(a => a.DeletedAt, (DateTime?)null));
        await db.SaveChangesAsync();
        _attachments.RaiseChanged();
    }

    /// <summary>Xóa vĩnh viễn thư mục đang ở thùng rác cùng thư mục con; tài liệu không còn nơi nào khác cũng bị xóa vĩnh viễn.</summary>
    public async Task PurgeFolderAsync(int id)
    {
        Access.Require(UserRole.Admin);
        await using var db = await _factory.CreateDbContextAsync();
        var all = await db.DocFolders.ToDictionaryAsync(f => f.Id);
        if (!all.TryGetValue(id, out var f) || !f.IsDeleted) throw new InvalidOperationException("Chỉ xóa vĩnh viễn thư mục đang ở thùng rác.");
        var subtree = Subtree(all, id);
        var fileIds = await db.AttachmentLinks.Where(l => l.FolderId != null && subtree.Contains(l.FolderId.Value)).Select(l => l.AttachmentId).Distinct().ToListAsync();
        await db.AttachmentLinks.Where(l => l.FolderId != null && subtree.Contains(l.FolderId.Value)).ExecuteDeleteAsync();
        // Xóa từ cấp sâu nhất lên (khóa ngoại thư mục cha)
        foreach (var sid in subtree.OrderByDescending(i => Depth(all, i)))
            await db.DocFolders.Where(x => x.Id == sid).ExecuteDeleteAsync();
        var orphans = await db.Attachments.Where(a => fileIds.Contains(a.Id) && !db.AttachmentLinks.Any(l => l.AttachmentId == a.Id)).Select(a => a.Id).ToListAsync();
        foreach (var aid in orphans) await PurgeFileCoreAsync(db, aid);
        _attachments.RaiseChanged();
    }

    // ---------------------------------------------------------------- Vị trí tài liệu

    /// <summary>Đặt thêm tài liệu vào một nơi khác (không sao chép tệp).</summary>
    public async Task AddPlacementAsync(int attachmentId, DocTarget target)
    {
        Access.Require(UserRole.Editor);
        AttachmentService.ValidateTarget(target);
        await using var db = await _factory.CreateDbContextAsync();
        if (!await db.Attachments.AnyAsync(a => a.Id == attachmentId && !a.IsDeleted)) throw new InvalidOperationException("Không tìm thấy tài liệu.");
        await AttachmentService.CheckTargetExistsAsync(db, target);
        if (await AttachmentService.HasPlacementAsync(db, attachmentId, target)) throw new ValidationException(new[] { "Tài liệu đã có ở nơi này." });
        var now = _clock.Now;
        AttachmentService.AddPlacement(db, attachmentId, target, now);
        var name = await db.Attachments.Where(a => a.Id == attachmentId).Select(a => a.Name).FirstAsync();
        AttachmentService.AddHistory(db, target, now, $"Gắn tài liệu: {name}");
        await db.SaveChangesAsync();
        _attachments.RaiseChanged();
    }

    /// <summary>Chuyển tài liệu từ một nơi sang nơi khác.</summary>
    public async Task MovePlacementAsync(int linkId, DocTarget target)
    {
        Access.Require(UserRole.Editor);
        AttachmentService.ValidateTarget(target);
        await using var db = await _factory.CreateDbContextAsync();
        var l = await db.AttachmentLinks.FirstOrDefaultAsync(x => x.Id == linkId) ?? throw new InvalidOperationException("Không tìm thấy vị trí tài liệu.");
        await AttachmentService.CheckTargetExistsAsync(db, target);
        if (await AttachmentService.HasPlacementAsync(db, l.AttachmentId, target)) { db.AttachmentLinks.Remove(l); }
        else
        {
            l.FolderId = target.FolderId;
            l.TaskId = target.TaskId;
            l.ProjectId = target.ProjectId;
            l.LegalDocumentId = target.LegalDocumentId;
        }
        await db.SaveChangesAsync();
        _attachments.RaiseChanged();
    }

    /// <summary>Gỡ tài liệu khỏi một nơi. Nếu đó là nơi cuối cùng, tài liệu vào thùng rác. Trả về true khi tài liệu vào thùng rác.</summary>
    public async Task<bool> RemovePlacementAsync(int linkId)
    {
        Access.Require(UserRole.Editor);
        await using var db = await _factory.CreateDbContextAsync();
        var l = await db.AttachmentLinks.FirstOrDefaultAsync(x => x.Id == linkId) ?? throw new InvalidOperationException("Không tìm thấy vị trí tài liệu.");
        var now = _clock.Now;
        var name = await db.Attachments.Where(a => a.Id == l.AttachmentId).Select(a => a.Name).FirstAsync();
        AttachmentService.AddHistory(db, new DocTarget(l.FolderId, l.TaskId, l.ProjectId, l.LegalDocumentId), now, $"Gỡ tài liệu: {name}");
        db.AttachmentLinks.Remove(l);
        await db.SaveChangesAsync();
        var trashed = await TrashOrphansAsync(db, new[] { l.AttachmentId }, now);
        _attachments.RaiseChanged();
        return trashed > 0;
    }

    // ---------------------------------------------------------------- Thùng rác

    public async Task TrashFileAsync(int id)
    {
        Access.Require(UserRole.Editor);
        await using var db = await _factory.CreateDbContextAsync();
        var n = await db.Attachments.Where(a => a.Id == id && !a.IsDeleted)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.IsDeleted, true).SetProperty(a => a.DeletedAt, _clock.Now));
        if (n == 0) throw new InvalidOperationException("Không tìm thấy tài liệu.");
        _attachments.RaiseChanged();
    }

    public async Task RestoreFileAsync(int id)
    {
        Access.Require(UserRole.Editor);
        await using var db = await _factory.CreateDbContextAsync();
        var a = await db.Attachments.FirstOrDefaultAsync(x => x.Id == id && x.IsDeleted) ?? throw new InvalidOperationException("Tài liệu không nằm trong thùng rác.");
        if (!await ActivePlacements(db, id).AnyAsync())
            throw new ValidationException(new[] { "Tài liệu chỉ nằm trong thư mục đã xóa hoặc đã bị gỡ khỏi mọi nơi. Hãy khôi phục thư mục chứa nó, hoặc xóa vĩnh viễn." });
        a.IsDeleted = false;
        a.DeletedAt = null;
        await db.SaveChangesAsync();
        _attachments.RaiseChanged();
    }

    /// <summary>Xóa vĩnh viễn tài liệu đang ở thùng rác: mọi phiên bản và tệp trên máy chủ (tệp gốc trên máy người dùng không bị ảnh hưởng).</summary>
    public async Task PurgeFileAsync(int id)
    {
        Access.Require(UserRole.Admin);
        await using var db = await _factory.CreateDbContextAsync();
        if (!await db.Attachments.AnyAsync(a => a.Id == id && a.IsDeleted)) throw new InvalidOperationException("Chỉ xóa vĩnh viễn tài liệu đang ở thùng rác.");
        await PurgeFileCoreAsync(db, id);
        _attachments.RaiseChanged();
    }

    private async Task PurgeFileCoreAsync(AppDbContext db, int id)
    {
        var a = await db.Attachments.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (a == null) return;
        var files = await db.AttachmentVersions.Where(v => v.AttachmentId == id).Select(v => v.StoredFile).ToListAsync();
        if (a.StoredFile != null) files.Add(a.StoredFile);
        await db.AttachmentLinks.Where(l => l.AttachmentId == id).ExecuteDeleteAsync();
        await db.AttachmentVersions.Where(v => v.AttachmentId == id).ExecuteDeleteAsync();
        await db.Attachments.Where(x => x.Id == id).ExecuteDeleteAsync();
        await _attachments.DeleteStoredIfUnusedAsync(db, files);
    }

    public async Task<TrashContent> GetTrashAsync()
    {
        Access.Require(UserRole.Editor);
        await using var db = await _factory.CreateDbContextAsync();
        var all = await db.DocFolders.AsNoTracking().ToDictionaryAsync(f => f.Id);
        // Chỉ thư mục bị xóa trực tiếp (thư mục cha chưa bị xóa)
        var folders = all.Values.Where(f => f.IsDeleted && !(f.ParentId is int p && all.TryGetValue(p, out var pf) && pf.IsDeleted && pf.DeletedAt == f.DeletedAt))
            .OrderByDescending(f => f.DeletedAt).ToList();
        var files = await AttachmentService.Light(db.Attachments.AsNoTracking().Where(a => a.IsDeleted)).OrderByDescending(a => a.DeletedAt).ToListAsync();
        return new TrashContent { Folders = folders, Files = await AttachmentService.BuildRowsAsync(db, files) };
    }

    // ---------------------------------------------------------------- Khi xóa công việc, dự án, văn bản

    /// <summary>
    /// Gỡ hồ sơ khỏi chủ sở hữu bị xóa vĩnh viễn: bỏ các vị trí trỏ tới nó, chuyển thư mục của nó thành thư mục chung
    /// (thêm tiền tố tên để còn nhận ra), tài liệu không còn nơi nào khác vào thùng rác. Không xóa tệp nào.
    /// </summary>
    internal static async Task DetachOwnerAsync(AppDbContext db, DocOwner owner, string label, DateTime now)
    {
        var links = db.AttachmentLinks.Where(l => l.FolderId == null && l.TaskId == owner.TaskId && l.ProjectId == owner.ProjectId && l.LegalDocumentId == owner.LegalDocumentId);
        var ids = await links.Select(l => l.AttachmentId).Distinct().ToListAsync();
        await links.ExecuteDeleteAsync();
        var folders = await db.DocFolders.Where(f => f.ProjectId == owner.ProjectId && f.TaskId == owner.TaskId && f.LegalDocumentId == owner.LegalDocumentId).ToListAsync();
        foreach (var f in folders)
        {
            if (f.ParentId == null) f.Name = $"[{label}] {f.Name}";
            f.ProjectId = f.TaskId = f.LegalDocumentId = null;
        }
        await db.SaveChangesAsync();
        await TrashOrphansAsync(db, ids, now);
    }

    // ---------------------------------------------------------------- Hỗ trợ

    private static IQueryable<AttachmentLink> ActivePlacements(AppDbContext db, int attachmentId) =>
        db.AttachmentLinks.Where(l => l.AttachmentId == attachmentId && (l.FolderId == null || db.DocFolders.Any(f => f.Id == l.FolderId && !f.IsDeleted)));

    /// <summary>Đưa vào thùng rác các tài liệu không còn nơi đặt nào đang hoạt động. Trả về số tài liệu vào thùng rác.</summary>
    private static async Task<int> TrashOrphansAsync(AppDbContext db, IEnumerable<int> ids, DateTime now)
    {
        int n = 0;
        foreach (var id in ids.Distinct())
        {
            if (await ActivePlacements(db, id).AnyAsync()) continue;
            n += await db.Attachments.Where(a => a.Id == id && !a.IsDeleted)
                .ExecuteUpdateAsync(s => s.SetProperty(a => a.IsDeleted, true).SetProperty(a => a.DeletedAt, now));
        }
        return n;
    }

    private static async Task<int> TrashCountAsync(AppDbContext db) =>
        await db.Attachments.CountAsync(a => a.IsDeleted) +
        await db.DocFolders.CountAsync(f => f.IsDeleted && (f.ParentId == null || !db.DocFolders.Any(p => p.Id == f.ParentId && p.IsDeleted && p.DeletedAt == f.DeletedAt)));

    /// <summary>Công việc (chưa xóa) có tài liệu gắn trực tiếp hoặc có thư mục.</summary>
    private static async Task<List<(int Id, int? ProjectId)>> TaskIdsWithDocsAsync(AppDbContext db)
    {
        var fromLinks = db.AttachmentLinks.Where(l => l.TaskId != null && db.Attachments.Any(a => a.Id == l.AttachmentId && !a.IsDeleted)).Select(l => l.TaskId!.Value);
        var fromFolders = db.DocFolders.Where(f => f.TaskId != null && !f.IsDeleted).Select(f => f.TaskId!.Value);
        var ids = fromLinks.Union(fromFolders);
        return (await db.Tasks.AsNoTracking().Where(t => ids.Contains(t.Id) && !t.IsDeleted).Select(t => new { t.Id, t.ProjectId }).ToListAsync())
            .Select(t => (t.Id, t.ProjectId)).ToList();
    }

    private static async Task<Dictionary<int, int>> TopFolderCountsAsync(AppDbContext db,
        System.Linq.Expressions.Expression<Func<DocFolder, bool>> filter, System.Linq.Expressions.Expression<Func<DocFolder, int>> key) =>
        await db.DocFolders.Where(f => !f.IsDeleted && f.ParentId == null).Where(filter).GroupBy(key)
            .Select(g => new { g.Key, N = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.N);

    private static async Task<Dictionary<int, int>> DirectFileCountsAsync(AppDbContext db,
        System.Linq.Expressions.Expression<Func<AttachmentLink, bool>> filter, System.Linq.Expressions.Expression<Func<AttachmentLink, int>> key) =>
        await db.AttachmentLinks.Where(l => l.FolderId == null && db.Attachments.Any(a => a.Id == l.AttachmentId && !a.IsDeleted)).Where(filter).GroupBy(key)
            .Select(g => new { g.Key, N = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.N);

    private static async Task<List<FolderInfo>> FolderInfosAsync(AppDbContext db, DocOwner owner, int? parentId)
    {
        var q = db.DocFolders.AsNoTracking().Where(f => !f.IsDeleted && f.ParentId == parentId);
        if (parentId == null) q = q.Where(f => f.ProjectId == owner.ProjectId && f.TaskId == owner.TaskId && f.LegalDocumentId == owner.LegalDocumentId);
        var folders = await q.OrderBy(f => f.Name).ToListAsync();
        var ids = folders.Select(f => f.Id).ToList();
        var subs = await db.DocFolders.Where(f => !f.IsDeleted && f.ParentId != null && ids.Contains(f.ParentId.Value)).GroupBy(f => f.ParentId!.Value)
            .Select(g => new { g.Key, N = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.N);
        var files = await db.AttachmentLinks.Where(l => l.FolderId != null && ids.Contains(l.FolderId.Value) && db.Attachments.Any(a => a.Id == l.AttachmentId && !a.IsDeleted))
            .GroupBy(l => l.FolderId!.Value).Select(g => new { g.Key, N = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.N);
        return folders.Select(f => new FolderInfo(f, subs.GetValueOrDefault(f.Id), files.GetValueOrDefault(f.Id))).ToList();
    }

    private static string CleanName(string? name)
    {
        name = (name ?? "").Trim();
        var errors = new List<string>();
        if (name.Length == 0) errors.Add("Vui lòng nhập tên thư mục.");
        if (name.Length > 300) errors.Add("Tên thư mục tối đa 300 ký tự.");
        if (errors.Count > 0) throw new ValidationException(errors);
        return name;
    }

    private static async Task CheckNameFreeAsync(AppDbContext db, string name, int? parentId, DocOwner owner, int? exceptId)
    {
        var siblings = db.DocFolders.Where(f => !f.IsDeleted && f.ParentId == parentId && f.Id != exceptId);
        if (parentId == null) siblings = siblings.Where(f => f.ProjectId == owner.ProjectId && f.TaskId == owner.TaskId && f.LegalDocumentId == owner.LegalDocumentId);
        var key = TextNormalizer.Normalize(name);
        if ((await siblings.Select(f => f.Name).ToListAsync()).Any(n => TextNormalizer.Normalize(n) == key))
            throw new ValidationException(new[] { $"Đã có thư mục \"{name}\" ở cùng cấp." });
    }

    private static async Task CheckOwnerAsync(AppDbContext db, DocOwner o)
    {
        bool ok = o.IsCommon
            || (o.ProjectId is int p && await db.Projects.AnyAsync(x => x.Id == p))
            || (o.TaskId is int t && await db.Tasks.AnyAsync(x => x.Id == t && !x.IsDeleted))
            || (o.LegalDocumentId is int l && await db.LegalDocuments.AnyAsync(x => x.Id == l && x.DocType == LegalDocType.Decision));
        if (!ok) throw new InvalidOperationException("Không tìm thấy dự án, công việc hoặc quyết định chứa thư mục.");
    }

    private static HashSet<int> Subtree(Dictionary<int, DocFolder> all, int id)
    {
        var result = new HashSet<int> { id };
        var queue = new Queue<int>(new[] { id });
        while (queue.Count > 0)
        {
            var cur = queue.Dequeue();
            foreach (var c in all.Values.Where(f => f.ParentId == cur))
                if (result.Add(c.Id)) queue.Enqueue(c.Id);
        }
        return result;
    }

    /// <summary>Cấp của thư mục (thư mục gốc = 1).</summary>
    private static int Depth(Dictionary<int, DocFolder> all, int id)
    {
        int d = 0;
        var seen = new HashSet<int>();
        for (int? cur = id; cur is int c && all.TryGetValue(c, out var f) && seen.Add(c); cur = f.ParentId) d++;
        return d;
    }

    /// <summary>Số cấp của cây con tính từ thư mục (chỉ thư mục đó = 1).</summary>
    private static int Height(Dictionary<int, DocFolder> all, int id)
    {
        var children = all.Values.Where(f => f.ParentId == id).ToList();
        return 1 + (children.Count == 0 ? 0 : children.Max(c => Height(all, c.Id)));
    }
}
