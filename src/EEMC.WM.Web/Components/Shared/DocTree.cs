using EEMC.WM.Core.Domain;
using EEMC.WM.Data.Services;

namespace EEMC.WM.Web.Components.Shared;

/// <summary>
/// Nguồn nút cho cây hồ sơ: Theo dự án (dự án → công việc có hồ sơ → thư mục), Theo quyết định, Công việc ngoài dự án,
/// Thư mục chung, Thùng rác. Khóa nút: g:* nhóm, p:/t:/d: thư mục gốc của dự án/công việc/quyết định, pt: nhóm công việc
/// của dự án, f: thư mục, trash: thùng rác.
/// </summary>
public sealed class DocTree
{
    private readonly DocumentService _docs;
    private readonly TaskService _tasks;
    private readonly bool _showTrash;

    public DocTree(DocumentService docs, TaskService tasks, bool showTrash)
    {
        _docs = docs;
        _tasks = tasks;
        _showTrash = showTrash;
    }

    /// <summary>Chủ sở hữu và thư mục ứng với khóa nút; null với nút nhóm và thùng rác.</summary>
    public static (DocOwner Owner, int? FolderId)? Container(string? key)
    {
        if (key == "g:common") return (DocOwner.Common, null);
        if (key == null || !key.Contains(':') || !int.TryParse(key[(key.IndexOf(':') + 1)..], out var id)) return null;
        return key[..key.IndexOf(':')] switch
        {
            "p" => (new DocOwner(ProjectId: id), null),
            "t" => (new DocOwner(TaskId: id), null),
            "d" => (new DocOwner(LegalDocumentId: id), null),
            "f" => (DocOwner.Common, id), // chủ sở hữu thật của thư mục lấy từ CSDL khi cần
            _ => null
        };
    }

    /// <summary>Nơi đặt tài liệu ứng với khóa nút (thư mục hoặc thư mục gốc của dự án/công việc/quyết định).</summary>
    public static DocTarget? Target(string? key)
    {
        var c = Container(key);
        if (c == null) return null;
        return c.Value.FolderId is int f ? DocTarget.Folder(f) : c.Value.Owner.AsTarget;
    }

    public static string KeyOf(DocTarget t) =>
        t.FolderId is int f ? $"f:{f}" : t.TaskId is int tk ? $"t:{tk}" : t.ProjectId is int p ? $"p:{p}" : $"d:{t.LegalDocumentId}";

    public static string KeyOf(DocOwner o) => o.ProjectId is int p ? $"p:{p}" : o.TaskId is int t ? $"t:{t}" : o.LegalDocumentId is int l ? $"d:{l}" : "g:common";

    public async Task<List<TreeNode>> LoadAsync(TreeNode? parent)
    {
        if (parent == null)
        {
            var c = await _docs.GetRootCountsAsync();
            var roots = new List<TreeNode>
            {
                new() { Key = "g:proj", Label = "Theo dự án KH&CN", Icon = "project", ChildCount = c.Projects },
                new() { Key = "g:dec", Label = "Theo quyết định", Icon = "law", ChildCount = c.Decisions },
            };
            if (c.LooseTasks > 0) roots.Add(new() { Key = "g:loose", Label = "Công việc ngoài dự án", Icon = "list", ChildCount = c.LooseTasks });
            roots.Add(new() { Key = "g:common", Label = "Thư mục chung", Icon = "folder", ChildCount = c.CommonFolders });
            if (_showTrash) roots.Add(new() { Key = "trash", Label = "Thùng rác", Icon = "trash", Meta = c.Trash > 0 ? $"{c.Trash}" : null });
            return roots;
        }
        var key = parent.Key;
        if (key == "g:proj")
            return (await _docs.GetProjectsAsync()).Select(x => new TreeNode
            {
                Key = $"p:{x.Project.Id}", Label = $"{x.Project.Code} – {x.Project.Name}", Icon = "project",
                ChildCount = x.Folders + (x.TasksWithDocs > 0 ? 1 : 0), Meta = Files(x.Files)
            }).ToList();
        if (key == "g:dec")
            return (await _docs.GetDecisionsAsync()).Select(x => new TreeNode
            {
                Key = $"d:{x.Decision.Id}", Label = x.Decision.ShortName, Icon = "law", ChildCount = x.Folders, Meta = Files(x.Files)
            }).ToList();
        if (key == "g:loose") return await TasksAsync(null);
        if (key.StartsWith("pt:")) return await TasksAsync(int.Parse(key[3..]));
        if (key == "g:common") return await FoldersAsync(DocOwner.Common, null);
        if (key.StartsWith("p:"))
        {
            var pid = int.Parse(key[2..]);
            var nodes = new List<TreeNode>();
            var tasks = (await _docs.GetTasksWithDocsAsync(pid)).Count;
            if (tasks > 0) nodes.Add(new TreeNode { Key = $"pt:{pid}", Label = "Hồ sơ theo công việc", Icon = "list", ChildCount = tasks });
            nodes.AddRange(await FoldersAsync(new DocOwner(ProjectId: pid), null));
            return nodes;
        }
        if (key.StartsWith("t:")) return await FoldersAsync(new DocOwner(TaskId: int.Parse(key[2..])), null);
        if (key.StartsWith("d:")) return await FoldersAsync(new DocOwner(LegalDocumentId: int.Parse(key[2..])), null);
        if (key.StartsWith("f:")) return await FoldersAsync(DocOwner.Common, int.Parse(key[2..]));
        return new();
    }

    private async Task<List<TreeNode>> TasksAsync(int? projectId) =>
        (await _docs.GetTasksWithDocsAsync(projectId)).Select(x => new TreeNode
        {
            Key = $"t:{x.Task.Id}", Label = $"{x.Task.Code} – {x.Task.Title}", Icon = "list", ChildCount = x.Folders, Meta = Files(x.Files)
        }).ToList();

    private async Task<List<TreeNode>> FoldersAsync(DocOwner owner, int? parentId) =>
        (await _docs.GetFoldersAsync(owner, parentId)).Select(x => new TreeNode
        {
            Key = $"f:{x.Folder.Id}", Label = x.Folder.Name, Icon = "folder", ChildCount = x.SubFolders, Meta = Files(x.Files)
        }).ToList();

    private static string? Files(int n) => n > 0 ? $"{n} tệp" : null;

    /// <summary>Chuỗi khóa từ nút gốc tới nút <paramref name="key"/> để mở sẵn nhánh.</summary>
    public async Task<List<string>> PathAsync(string key)
    {
        if (key.StartsWith("f:"))
        {
            var chain = await _docs.GetFolderPathAsync(int.Parse(key[2..]));
            if (chain.Count == 0) return new();
            var path = await PathAsync(KeyOf(DocOwner.Of(chain[0])));
            path.AddRange(chain.Select(f => $"f:{f.Id}"));
            return path;
        }
        if (key.StartsWith("p:")) return new() { "g:proj", key };
        if (key.StartsWith("d:")) return new() { "g:dec", key };
        if (key.StartsWith("t:"))
        {
            var t = await _tasks.GetAsync(int.Parse(key[2..]));
            if (t == null) return new();
            return t.ProjectId is int p ? new() { "g:proj", $"p:{p}", $"pt:{p}", key } : new() { "g:loose", key };
        }
        return new() { key };
    }

    /// <summary>Tìm thư mục theo tên và nơi chứa tài liệu có tên/nội dung khớp.</summary>
    public async Task<List<IReadOnlyList<string>>> SearchAsync(string q)
    {
        var (folders, placements) = await _docs.SearchAsync(q);
        var keys = folders.Select(f => $"f:{f.Id}")
            .Concat(placements.Select(l => KeyOf(new DocTarget(l.FolderId, l.TaskId, l.ProjectId, l.LegalDocumentId))))
            .Distinct().Take(30).ToList();
        var result = new List<IReadOnlyList<string>>();
        foreach (var k in keys)
        {
            var p = await PathAsync(k);
            if (p.Count > 0) result.Add(p);
        }
        return result;
    }
}
