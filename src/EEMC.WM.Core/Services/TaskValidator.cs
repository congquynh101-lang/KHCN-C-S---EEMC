using EEMC.WM.Core.Domain;

namespace EEMC.WM.Core.Services;

public static class TaskValidator
{
    public const int MaxTitleLength = 2000;

    public static List<string> Validate(WorkTask t)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(t.Title)) errors.Add("Vui lòng nhập nội dung công việc.");
        else if (t.Title.Length > MaxTitleLength) errors.Add($"Nội dung công việc tối đa {MaxTitleLength} ký tự.");
        if (t.ReceivedAt == default) errors.Add("Vui lòng nhập thời gian nhận công việc.");
        if (t.DueAt == default) errors.Add("Vui lòng nhập hạn hoàn thành.");
        if (t.ReceivedAt != default && t.DueAt != default && t.DueAt < t.ReceivedAt)
            errors.Add("Hạn hoàn thành không được trước thời gian nhận việc.");
        if (!Enum.IsDefined(t.Priority)) errors.Add("Vui lòng chọn mức độ ưu tiên.");
        if (!Enum.IsDefined(t.Status)) errors.Add("Trạng thái không hợp lệ.");
        if (t.Progress is < 0 or > 100) errors.Add("Tiến độ phải trong khoảng 0–100%.");
        if (t.EstimatedHours is < 0 or > 2000) errors.Add("Thời lượng dự kiến phải trong khoảng 0–2000 giờ.");
        if (t.IsFixedTime && !t.PlannedStart.HasValue)
            errors.Add("Công việc có thời điểm cố định cần nhập thời gian bắt đầu theo kế hoạch.");
        return errors;
    }
}
