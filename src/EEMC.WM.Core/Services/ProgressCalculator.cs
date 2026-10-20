using EEMC.WM.Core.Domain;

namespace EEMC.WM.Core.Services;

/// <summary>Tính tiến độ tổng thể từ các công việc con (có trọng số do người dùng đặt).</summary>
public static class ProgressCalculator
{
    /// <summary>
    /// Trung bình có trọng số của tiến độ các công việc con. Công việc hoàn thành tính 100%,
    /// công việc hủy hoặc đã xóa không tính. Trả về null nếu không có công việc con hợp lệ.
    /// </summary>
    public static int? Weighted(IEnumerable<WorkTask> children)
    {
        double sumW = 0, sum = 0;
        foreach (var c in children)
        {
            if (c.IsDeleted || c.Status == WorkTaskStatus.Cancelled) continue;
            var w = c.Weight > 0 ? c.Weight : 0;
            if (w == 0) continue;
            var p = c.Status == WorkTaskStatus.Completed ? 100 : Math.Clamp(c.Progress, 0, 100);
            sumW += w;
            sum += w * p;
        }
        return sumW <= 0 ? null : (int)Math.Round(sum / sumW, MidpointRounding.AwayFromZero);
    }

    /// <summary>Kiểm tra thêm phụ thuộc a → b (a chờ b) có tạo vòng lặp không.</summary>
    public static bool CreatesCycle(int taskId, int dependsOnId, IReadOnlyCollection<(int TaskId, int DependsOnId)> edges)
    {
        if (taskId == dependsOnId) return true;
        // Có đường đi từ dependsOnId tới taskId theo chiều "chờ" thì sẽ thành vòng
        var stack = new Stack<int>();
        var seen = new HashSet<int>();
        stack.Push(dependsOnId);
        while (stack.Count > 0)
        {
            var cur = stack.Pop();
            if (cur == taskId) return true;
            if (!seen.Add(cur)) continue;
            foreach (var e in edges.Where(e => e.TaskId == cur)) stack.Push(e.DependsOnId);
        }
        return false;
    }
}
