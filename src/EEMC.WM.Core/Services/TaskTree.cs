using EEMC.WM.Core.Domain;

namespace EEMC.WM.Core.Services;

/// <summary>Sắp công việc theo cây cha – con (nhiều cấp) để hiển thị thụt lề.</summary>
public static class TaskTree
{
    /// <summary>
    /// Trả về danh sách theo thứ tự cây: mỗi công việc đứng ngay trước các công việc con của nó.
    /// Level = 0 cho công việc gốc (hoặc có cha không nằm trong danh sách), 1 cho con, 2 cho cháu...
    /// </summary>
    public static List<(WorkTask Task, int Level)> Flatten<TKey>(IEnumerable<WorkTask> tasks, Func<WorkTask, TKey> orderBy)
    {
        var list = tasks.ToList();
        var ids = list.Select(t => t.Id).ToHashSet();
        var byParent = list.Where(t => t.ParentId.HasValue && ids.Contains(t.ParentId.Value)).ToLookup(t => t.ParentId!.Value);
        var result = new List<(WorkTask, int)>();
        var seen = new HashSet<int>();

        void Add(WorkTask t, int level)
        {
            if (!seen.Add(t.Id)) return;
            result.Add((t, level));
            foreach (var c in byParent[t.Id].OrderBy(orderBy).ThenBy(c => c.Id)) Add(c, level + 1);
        }

        foreach (var r in list.Where(t => t.ParentId == null || !ids.Contains(t.ParentId.Value)).OrderBy(orderBy).ThenBy(t => t.Id))
            Add(r, 0);
        // Công việc nằm trong vòng lặp cha – con (dữ liệu lỗi) vẫn được hiển thị
        foreach (var t in list.Where(t => !seen.Contains(t.Id)).OrderBy(orderBy).ThenBy(t => t.Id))
            Add(t, 0);
        return result;
    }
}
