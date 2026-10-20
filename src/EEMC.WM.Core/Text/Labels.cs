using EEMC.WM.Core.Domain;

namespace EEMC.WM.Core.Text;

/// <summary>Nhãn tiếng Việt cho các giá trị liệt kê và định dạng thời gian.</summary>
public static class Labels
{
    public const string DateFormat = "dd/MM/yyyy";
    public const string TimeFormat = "HH:mm";
    public const string DateTimeFormat = "dd/MM/yyyy HH:mm";

    public static string Of(TaskPriority p) => p switch
    {
        TaskPriority.Urgent => "Khẩn cấp",
        TaskPriority.High => "Cao",
        TaskPriority.Normal => "Bình thường",
        TaskPriority.Low => "Thấp",
        _ => p.ToString()
    };

    public static string Of(WorkTaskStatus s) => s switch
    {
        WorkTaskStatus.NotStarted => "Chưa thực hiện",
        WorkTaskStatus.InProgress => "Đang thực hiện",
        WorkTaskStatus.WaitingCoordinator => "Chờ đơn vị phối hợp",
        WorkTaskStatus.WaitingResponse => "Chờ phản hồi",
        WorkTaskStatus.WaitingApproval => "Chờ lãnh đạo phê duyệt",
        WorkTaskStatus.NeedMoreDocuments => "Cần bổ sung hồ sơ",
        WorkTaskStatus.OnHold => "Tạm dừng",
        WorkTaskStatus.Completed => "Hoàn thành",
        WorkTaskStatus.Cancelled => "Hủy",
        _ => s.ToString()
    };

    public static string Of(DocumentStatus d) => d switch
    {
        DocumentStatus.None => "Chưa có hồ sơ",
        DocumentStatus.Preparing => "Đang chuẩn bị",
        DocumentStatus.Reviewing => "Đang rà soát",
        DocumentStatus.Submitted => "Đã trình ký",
        DocumentStatus.Approved => "Đã phê duyệt",
        DocumentStatus.Archived => "Đã lưu trữ",
        _ => d.ToString()
    };

    public static string Of(UrgencyLevel u) => u switch
    {
        UrgencyLevel.Overdue => "Quá hạn",
        UrgencyLevel.DueToday => "Đến hạn hôm nay",
        UrgencyLevel.DueSoon => "Sắp đến hạn",
        UrgencyLevel.ThisWeek => "Trong tuần",
        UrgencyLevel.Later => "Còn thời gian",
        _ => u.ToString()
    };

    public static string Of(EisenhowerQuadrant q) => q switch
    {
        EisenhowerQuadrant.DoNow => "Làm ngay (Quan trọng – Khẩn cấp)",
        EisenhowerQuadrant.Schedule => "Lên kế hoạch (Quan trọng – Chưa khẩn cấp)",
        EisenhowerQuadrant.QuickHandle => "Xử lý nhanh (Ít quan trọng – Khẩn cấp)",
        EisenhowerQuadrant.Later => "Xem xét sau (Ít quan trọng – Chưa khẩn cấp)",
        _ => q.ToString()
    };

    public static string Of(ProjectStatus s) => s switch
    {
        ProjectStatus.Proposed => "Đề xuất",
        ProjectStatus.Preparing => "Chuẩn bị hồ sơ",
        ProjectStatus.InProgress => "Đang thực hiện",
        ProjectStatus.Acceptance => "Nghiệm thu",
        ProjectStatus.Settlement => "Thanh quyết toán",
        ProjectStatus.Completed => "Hoàn thành",
        ProjectStatus.Suspended => "Tạm dừng",
        _ => s.ToString()
    };

    public static string Of(AttachmentKind k) => k switch
    {
        AttachmentKind.Word => "Word",
        AttachmentKind.Excel => "Excel",
        AttachmentKind.Pdf => "PDF",
        AttachmentKind.Folder => "Thư mục hồ sơ",
        AttachmentKind.Link => "Đường dẫn/Liên kết",
        AttachmentKind.Minutes => "Biên bản họp",
        AttachmentKind.Decision => "Quyết định",
        AttachmentKind.Proposal => "Tờ trình",
        AttachmentKind.Report => "Báo cáo",
        _ => "Tài liệu khác"
    };

    /// <summary>"Còn 2 ngày 3 giờ", "Quá hạn 5 giờ", "Còn 45 phút".</summary>
    public static string TimeRemaining(DateTime due, DateTime now)
    {
        var diff = due - now;
        bool overdue = diff < TimeSpan.Zero;
        var a = overdue ? -diff : diff;
        string body;
        if (a.TotalMinutes < 1) body = "dưới 1 phút";
        else if (a.TotalHours < 1) body = $"{(int)a.TotalMinutes} phút";
        else if (a.TotalDays < 1) body = a.Minutes > 0 && a.TotalHours < 10 ? $"{(int)a.TotalHours} giờ {a.Minutes} phút" : $"{(int)a.TotalHours} giờ";
        else body = a.Hours > 0 ? $"{(int)a.TotalDays} ngày {a.Hours} giờ" : $"{(int)a.TotalDays} ngày";
        return overdue ? $"Quá hạn {body}" : $"Còn {body}";
    }

    public static string Date(DateTime d) => d.ToString(DateFormat);
    public static string DateTime(DateTime d) => d.ToString(DateTimeFormat);
    public static string DateTime(DateTime? d) => d.HasValue ? d.Value.ToString(DateTimeFormat) : "";

    public static string DayOfWeekVi(DayOfWeek d) => d switch
    {
        DayOfWeek.Monday => "Thứ Hai",
        DayOfWeek.Tuesday => "Thứ Ba",
        DayOfWeek.Wednesday => "Thứ Tư",
        DayOfWeek.Thursday => "Thứ Năm",
        DayOfWeek.Friday => "Thứ Sáu",
        DayOfWeek.Saturday => "Thứ Bảy",
        DayOfWeek.Sunday => "Chủ Nhật",
        _ => d.ToString()
    };

    public static string DayOfWeekShort(DayOfWeek d) => d switch
    {
        DayOfWeek.Monday => "T2",
        DayOfWeek.Tuesday => "T3",
        DayOfWeek.Wednesday => "T4",
        DayOfWeek.Thursday => "T5",
        DayOfWeek.Friday => "T6",
        DayOfWeek.Saturday => "T7",
        DayOfWeek.Sunday => "CN",
        _ => d.ToString()
    };
}
