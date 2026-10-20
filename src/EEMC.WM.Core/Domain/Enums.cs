namespace EEMC.WM.Core.Domain;

/// <summary>Mức độ ưu tiên do người dùng chọn. Phần mềm không tự ý thay đổi giá trị này.</summary>
public enum TaskPriority
{
    Low = 1,
    Normal = 2,
    High = 3,
    Urgent = 4
}

public enum WorkTaskStatus
{
    NotStarted = 1,
    InProgress = 2,
    WaitingCoordinator = 3,
    WaitingResponse = 4,
    WaitingApproval = 5,
    NeedMoreDocuments = 6,
    OnHold = 7,
    Completed = 8,
    Cancelled = 9
}

public enum DocumentStatus
{
    None = 0,
    Preparing = 1,
    Reviewing = 2,
    Submitted = 3,
    Approved = 4,
    Archived = 5
}

/// <summary>Mức khẩn cấp được tính từ thời hạn thực tế (khác với mức ưu tiên người dùng chọn).</summary>
public enum UrgencyLevel
{
    Later = 0,
    ThisWeek = 1,
    DueSoon = 2,
    DueToday = 3,
    Overdue = 4
}

/// <summary>Góc phần tư ma trận Eisenhower.</summary>
public enum EisenhowerQuadrant
{
    DoNow = 1,        // Quan trọng + Khẩn cấp
    Schedule = 2,     // Quan trọng + Chưa khẩn cấp
    QuickHandle = 3,  // Ít quan trọng + Khẩn cấp
    Later = 4         // Ít quan trọng + Chưa khẩn cấp
}

public enum NotificationSeverity
{
    Info = 0,
    Warning = 1,
    Critical = 2
}

public static class StatusGroups
{
    public static readonly WorkTaskStatus[] Waiting =
    {
        WorkTaskStatus.WaitingCoordinator,
        WorkTaskStatus.WaitingResponse,
        WorkTaskStatus.WaitingApproval
    };

    public static readonly WorkTaskStatus[] Closed =
    {
        WorkTaskStatus.Completed,
        WorkTaskStatus.Cancelled
    };

    public static bool IsWaiting(WorkTaskStatus s) => Array.IndexOf(Waiting, s) >= 0;
    public static bool IsClosed(WorkTaskStatus s) => s == WorkTaskStatus.Completed || s == WorkTaskStatus.Cancelled;
    /// <summary>Công việc người dùng đang trực tiếp xử lý (không phải chờ, không tạm dừng, chưa đóng).</summary>
    public static bool IsDirectWork(WorkTaskStatus s) =>
        s is WorkTaskStatus.NotStarted or WorkTaskStatus.InProgress or WorkTaskStatus.NeedMoreDocuments;
}
