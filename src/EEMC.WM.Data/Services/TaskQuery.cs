using EEMC.WM.Core.Domain;

namespace EEMC.WM.Data.Services;

public enum TimeFilter
{
    All,
    Overdue,
    Today,
    DueSoon,
    ThisWeek,
    ThisMonth,
    NextWeek,
    ReceivedThisWeek,
    CompletedThisWeek
}

public enum StatusFilter
{
    Open,          // Chưa hoàn thành (không gồm Hoàn thành/Hủy)
    DirectWork,    // Đang trực tiếp xử lý
    Waiting,       // Chờ phối hợp/phản hồi/phê duyệt
    All,
    Specific       // Dùng trường Status
}

public enum SortField
{
    Suggested,  // Theo thuật toán ưu tiên (tính trong bộ nhớ cho danh sách đã lọc)
    DueAt,
    Priority,
    ReceivedAt,
    Status,
    Code,
    Title,
    UpdatedAt
}

public sealed class TaskQuery
{
    public string? Search { get; set; }
    public StatusFilter StatusFilter { get; set; } = StatusFilter.Open;
    public WorkTaskStatus? Status { get; set; }
    public TaskPriority? Priority { get; set; }
    /// <summary>Lọc mức ưu tiên từ mức này trở lên (ví dụ Cao + Khẩn cấp).</summary>
    public TaskPriority? MinPriority { get; set; }
    public TimeFilter Time { get; set; } = TimeFilter.All;
    public int? CategoryGroupId { get; set; }
    /// <summary>Chỉ công việc có hồ sơ (đường dẫn, số văn bản hoặc tình trạng hồ sơ).</summary>
    public bool OnlyWithDocuments { get; set; }
    public DocumentStatus? DocumentStatus { get; set; }
    public bool Deleted { get; set; }
    public SortField Sort { get; set; } = SortField.DueAt;
    public bool SortDescending { get; set; }
    public SortField? ThenSort { get; set; }
    public bool ThenSortDescending { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 100;
}

public sealed class PagedResult<T>
{
    public required IReadOnlyList<T> Items { get; init; }
    public int Total { get; init; }
    public int Page { get; init; }
    public int PageSize { get; init; }
    public int PageCount => PageSize <= 0 ? 1 : Math.Max(1, (int)Math.Ceiling(Total / (double)PageSize));
}
