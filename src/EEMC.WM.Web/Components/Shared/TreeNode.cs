namespace EEMC.WM.Web.Components.Shared;

/// <summary>Một nút của <see cref="TreeView"/>. Con chỉ được tải khi người dùng mở nút (Children = null: chưa tải).</summary>
public sealed class TreeNode
{
    /// <summary>Khóa duy nhất trong cây, ví dụ "p:5" (dự án), "t:12" (công việc).</summary>
    public required string Key { get; init; }
    public required string Label { get; init; }
    public string? Href { get; init; }
    public string? Icon { get; init; }
    public string? Badge { get; init; }
    public string? BadgeColor { get; init; }
    /// <summary>Chữ phụ bên phải (hạn, ngày hiệu lực...).</summary>
    public string? Meta { get; init; }
    public string? MetaColor { get; init; }
    /// <summary>Số nút con; 0 = nút lá.</summary>
    public int ChildCount { get; set; }
    public object? Data { get; init; }
    public int Level { get; internal set; }

    public List<TreeNode>? Children { get; set; }
    public bool Expanded { get; set; }
    public bool Loading { get; set; }
    public bool Match { get; set; }
    public bool HasChildren => ChildCount > 0 || Children is { Count: > 0 };
}
