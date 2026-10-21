using EEMC.WM.Core.Domain;
using EEMC.WM.Core.Text;

namespace EEMC.WM.Web.Components.Shared;

/// <summary>Màu và nhãn hiển thị dùng chung: Đỏ quá hạn/khẩn cấp, Cam cao/sắp đến hạn, Xanh dương đang làm, Xám chờ, Xanh lá hoàn thành.</summary>
public static class Ui
{
    public static string PriorityColor(TaskPriority p) => p switch
    {
        TaskPriority.Urgent => "red",
        TaskPriority.High => "orange",
        TaskPriority.Normal => "blue",
        _ => "gray"
    };

    public static string StatusColor(WorkTaskStatus s) => s switch
    {
        WorkTaskStatus.Completed => "green",
        WorkTaskStatus.InProgress => "blue",
        WorkTaskStatus.NeedMoreDocuments => "orange",
        WorkTaskStatus.WaitingApproval => "purple",
        _ => "gray"
    };

    public static string DueColor(WorkTask t, DateTime now)
    {
        if (StatusGroups.IsClosed(t.Status)) return t.Status == WorkTaskStatus.Completed ? "green" : "gray";
        if (t.DueAt < now) return "red";
        if ((t.DueAt.Date - now.Date).TotalDays <= 3) return "orange";
        return "gray";
    }

    public static string Remaining(WorkTask t, DateTime now)
    {
        if (t.Status == WorkTaskStatus.Completed && t.CompletedAt.HasValue)
            return t.CompletedAt <= t.DueAt ? "Hoàn thành đúng hạn" : "Hoàn thành trễ hạn";
        if (t.Status == WorkTaskStatus.Cancelled) return "Đã hủy";
        return Labels.TimeRemaining(t.DueAt, now);
    }

    public static string ProjectStatusColor(ProjectStatus s) => s switch
    {
        ProjectStatus.Completed => "green",
        ProjectStatus.InProgress => "blue",
        ProjectStatus.Acceptance or ProjectStatus.Settlement => "purple",
        ProjectStatus.Suspended => "orange",
        _ => "gray"
    };

    public static string EffectColor(LegalEffect e) => e switch
    {
        LegalEffect.InEffect => "green",
        LegalEffect.PartlyExpired => "orange",
        LegalEffect.Expired => "red",
        LegalEffect.NotYetEffective => "blue",
        _ => "gray"
    };

    public static string SummaryColor(SummaryState s) => s switch
    {
        SummaryState.Approved => "green",
        SummaryState.Draft => "orange",
        _ => "gray"
    };

    /// <summary>Giá trị CSS luôn dùng dấu chấm thập phân (văn hóa vi-VN dùng dấu phẩy làm hỏng CSS).</summary>
    public static string Px(double v) => v.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + "px";
    public static string Pc(double v) => v.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + "%";

    public static string Dt(DateTime? d) => d.HasValue ? d.Value.ToString(Labels.DateTimeFormat) : "";
    public static string D(DateTime? d) => d.HasValue ? d.Value.ToString(Labels.DateFormat) : "";

    /// <summary>Định dạng giá trị cho input type=datetime-local / date.</summary>
    public static string ToInput(DateTime? d) => d.HasValue ? d.Value.ToString("yyyy-MM-ddTHH:mm") : "";
    public static string ToDateInput(DateTime? d) => d.HasValue ? d.Value.ToString("yyyy-MM-dd") : "";

    public static DateTime? FromInput(string? s) =>
        DateTime.TryParse(s, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var v) ? v : null;
}
