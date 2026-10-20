using EEMC.WM.Core.Domain;
using EEMC.WM.Core.Settings;
using EEMC.WM.Core.Text;

namespace EEMC.WM.Core.Services;

public sealed class ReminderMessage
{
    public string Title { get; init; } = "";
    public string Message { get; init; } = "";
    public NotificationSeverity Severity { get; init; }
}

public sealed class ReminderDecision
{
    /// <summary>Các khóa mốc nhắc cần ghi nhận là đã xử lý (kể cả các mốc bỏ qua để không nhắc dồn).</summary>
    public List<string> KeysToLog { get; } = new();
    /// <summary>Tối đa một thông báo cho mỗi công việc ở mỗi lần kiểm tra.</summary>
    public ReminderMessage? Message { get; set; }
    public bool IsEmpty => KeysToLog.Count == 0 && Message == null;
}

/// <summary>
/// Quyết định nhắc việc cho một công việc tại thời điểm hiện tại.
/// - Mỗi mốc chỉ nhắc một lần (theo khóa đã ghi nhận).
/// - Khi nhiều mốc cùng đã qua (ví dụ ứng dụng tắt lâu), chỉ nhắc mốc gần hạn nhất.
/// - Mốc đã qua trước thời điểm tạo công việc thì bỏ qua.
/// - Quá hạn: nhắc lặp theo chu kỳ cấu hình cho đến khi hoàn thành/hủy.
/// - Công việc chờ: nhắc đôn đốc từ ngày dự kiến phản hồi, mỗi ngày một lần.
/// - Tạm hoãn: không nhắc cho đến hết thời gian tạm hoãn, sau đó nhắc lại một lần.
/// </summary>
public static class ReminderPlanner
{
    public static ReminderDecision Decide(WorkTask t, WorkSettings settings, DateTime now, ISet<string> firedKeys)
    {
        var d = new ReminderDecision();
        if (t.IsDeleted || StatusGroups.IsClosed(t.Status)) return d;
        if (t.SnoozeUntil.HasValue && t.SnoozeUntil.Value > now) return d;

        string remaining = Labels.TimeRemaining(t.DueAt, now);
        string head = $"[{t.Code}] {t.Title}";

        // Nhắc lại sau khi hết tạm hoãn
        if (t.SnoozeUntil.HasValue)
        {
            var key = $"snooze-{t.SnoozeUntil.Value:yyyyMMddHHmm}";
            if (!firedKeys.Contains(key))
            {
                d.KeysToLog.Add(key);
                d.Message = new ReminderMessage
                {
                    Title = "Nhắc lại công việc đã tạm hoãn",
                    Message = $"{head}\nHạn: {Labels.DateTime(t.DueAt)} — {remaining}.",
                    Severity = t.DueAt < now ? NotificationSeverity.Critical : NotificationSeverity.Warning
                };
            }
        }

        var offsets = settings.ReminderOffsets.Where(o => o.Enabled).OrderBy(o => o.Minutes).ToList();

        if (t.DueAt <= now)
        {
            // Đã đến hạn: các mốc trước hạn coi như đã qua
            foreach (var o in offsets.Where(o => o.Minutes > 0))
                if (!firedKeys.Contains(o.Key)) d.KeysToLog.Add(o.Key);

            var atDue = offsets.FirstOrDefault(o => o.Minutes == 0);
            bool justDue = (now - t.DueAt).TotalMinutes < 60;

            if (justDue && atDue != null && !firedKeys.Contains(atDue.Key))
            {
                d.KeysToLog.Add(atDue.Key);
                d.Message ??= new ReminderMessage
                {
                    Title = "Công việc đến hạn",
                    Message = $"{head}\nĐến hạn lúc {t.DueAt:HH:mm dd/MM/yyyy}.",
                    Severity = NotificationSeverity.Critical
                };
            }
            else
            {
                if (atDue != null && !firedKeys.Contains(atDue.Key)) d.KeysToLog.Add(atDue.Key);
                if (settings.RemindWhenOverdue && !justDue)
                {
                    var period = (int)Math.Floor((now - t.DueAt).TotalHours / Math.Max(1, settings.OverdueRepeatHours));
                    var key = $"overdue-{period}";
                    if (!firedKeys.Contains(key))
                    {
                        d.KeysToLog.Add(key);
                        d.Message ??= new ReminderMessage
                        {
                            Title = "Công việc quá hạn chưa hoàn thành",
                            Message = $"{head}\nHạn: {Labels.DateTime(t.DueAt)} — {remaining}. Trạng thái: {Labels.Of(t.Status)}.",
                            Severity = NotificationSeverity.Critical
                        };
                    }
                }
            }
        }
        else
        {
            // Các mốc trước hạn đã qua
            var passed = offsets.Where(o => o.Minutes > 0 && now >= t.DueAt.AddMinutes(-o.Minutes)).ToList();
            if (passed.Count > 0)
            {
                var nearest = passed[0]; // mốc gần hạn nhất
                foreach (var o in passed.Skip(1))
                    if (!firedKeys.Contains(o.Key)) d.KeysToLog.Add(o.Key);

                if (!firedKeys.Contains(nearest.Key))
                {
                    d.KeysToLog.Add(nearest.Key);
                    // Mốc đã qua trước khi tạo công việc thì không nhắc
                    bool beforeCreation = t.DueAt.AddMinutes(-nearest.Minutes) < t.CreatedAt;
                    if (!beforeCreation)
                    {
                        d.Message ??= new ReminderMessage
                        {
                            Title = $"Nhắc việc: {nearest.Label.ToLowerInvariant()}",
                            Message = $"{head}\nHạn: {Labels.DateTime(t.DueAt)} — {remaining}. Ưu tiên: {Labels.Of(t.Priority)}.",
                            Severity = nearest.Minutes <= 240 ? NotificationSeverity.Warning : NotificationSeverity.Info
                        };
                    }
                }
            }
        }

        // Đôn đốc công việc chờ phản hồi
        if (settings.RemindFollowUp && StatusGroups.IsWaiting(t.Status) && t.ExpectedResponseAt.HasValue && t.ExpectedResponseAt.Value <= now)
        {
            var key = $"followup-{now:yyyyMMdd}";
            if (!firedKeys.Contains(key))
            {
                d.KeysToLog.Add(key);
                d.Message ??= new ReminderMessage
                {
                    Title = "Cần đôn đốc phản hồi",
                    Message = $"{head}\n{Labels.Of(t.Status)} — dự kiến phản hồi {Labels.DateTime(t.ExpectedResponseAt)}." +
                              (string.IsNullOrWhiteSpace(t.Coordinator) ? "" : $" Đơn vị: {t.Coordinator}."),
                    Severity = NotificationSeverity.Warning
                };
            }
        }

        return d;
    }
}
