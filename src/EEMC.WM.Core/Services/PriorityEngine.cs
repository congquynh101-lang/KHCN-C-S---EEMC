using EEMC.WM.Core.Domain;
using EEMC.WM.Core.Settings;
using EEMC.WM.Core.Text;

namespace EEMC.WM.Core.Services;

public sealed class PriorityReason
{
    public PriorityReason(string text, double points) { Text = text; Points = points; }
    public string Text { get; }
    public double Points { get; }
    public override string ToString() => Points > 0 ? $"{Text} (+{Points:0})" : Points < 0 ? $"{Text} ({Points:0})" : Text;
}

/// <summary>Kết quả đánh giá ưu tiên của một công việc. Không thay đổi mức ưu tiên người dùng đặt.</summary>
public sealed class PriorityAssessment
{
    public required WorkTask Task { get; init; }
    public double Score { get; init; }
    public UrgencyLevel Urgency { get; init; }
    public EisenhowerQuadrant Quadrant { get; init; }
    /// <summary>Mức ưu tiên đề xuất (chỉ để tham khảo).</summary>
    public TaskPriority SuggestedPriority { get; init; }
    public IReadOnlyList<PriorityReason> Reasons { get; init; } = Array.Empty<PriorityReason>();
    /// <summary>Giờ làm việc còn lại đến hạn.</summary>
    public double RemainingWorkHours { get; init; }
    /// <summary>Số giờ cần để hoàn thành phần còn lại.</summary>
    public double RequiredHours { get; init; }
    /// <summary>True nếu RequiredHours lấy từ thời lượng mặc định (chưa có ước tính thật).</summary>
    public bool RequiredHoursIsAssumed { get; init; }
    /// <summary>Có nguy cơ không đủ thời gian (khi ước tính là giả định thì chỉ là cảnh báo, không khẳng định).</summary>
    public bool TimeRisk { get; init; }
    public bool IsWaiting { get; init; }
    public bool IsOnHold { get; init; }

    public bool SuggestionDiffers => SuggestedPriority != Task.Priority;
    public string Explanation => string.Join(" • ", Reasons.Select(r => r.ToString()));
}

/// <summary>
/// Thuật toán xác định thứ tự ưu tiên: kết hợp mức quan trọng (người dùng chọn), mức khẩn cấp
/// theo thời hạn thực tế, áp lực thời gian (thời lượng dự kiến so với giờ làm việc còn lại),
/// công việc cố định trong ngày và trạng thái chờ.
/// </summary>
public sealed class PriorityEngine
{
    private readonly WorkSettings _settings;
    private readonly WorkCalendar _calendar;

    public PriorityEngine(WorkSettings settings)
    {
        _settings = settings;
        _calendar = new WorkCalendar(settings);
    }

    public static double ImportancePoints(TaskPriority p) => p switch
    {
        TaskPriority.Urgent => 40,
        TaskPriority.High => 30,
        TaskPriority.Normal => 20,
        _ => 10
    };

    public UrgencyLevel GetUrgency(DateTime due, DateTime now)
    {
        if (due < now) return UrgencyLevel.Overdue;
        var days = (due.Date - now.Date).Days;
        if (days == 0) return UrgencyLevel.DueToday;
        if (days <= _settings.DueSoonDays) return UrgencyLevel.DueSoon;
        if (days <= 7) return UrgencyLevel.ThisWeek;
        return UrgencyLevel.Later;
    }

    public PriorityAssessment Assess(WorkTask t, DateTime now)
    {
        var reasons = new List<PriorityReason>();
        double score = 0;

        // 1. Mức quan trọng do người dùng chọn
        var imp = ImportancePoints(t.Priority);
        score += imp;
        reasons.Add(new PriorityReason($"Mức ưu tiên {Labels.Of(t.Priority)}", imp));

        // 2. Mức khẩn cấp theo thời hạn thực tế
        var urgency = GetUrgency(t.DueAt, now);
        double up;
        switch (urgency)
        {
            case UrgencyLevel.Overdue:
                var overdueDays = (now - t.DueAt).TotalDays;
                up = 50 + Math.Min(Math.Floor(overdueDays) * 3, 15);
                reasons.Add(new PriorityReason($"Đã {Labels.TimeRemaining(t.DueAt, now).ToLowerInvariant()}", up));
                break;
            case UrgencyLevel.DueToday:
                up = (t.DueAt - now).TotalHours <= 4 ? 45 : 40;
                reasons.Add(new PriorityReason($"Đến hạn hôm nay lúc {t.DueAt:HH:mm}", up));
                break;
            case UrgencyLevel.DueSoon:
                var d = (t.DueAt.Date - now.Date).Days;
                up = Math.Max(20, 36 - 4 * d);
                reasons.Add(new PriorityReason(d == 1 ? "Hạn vào ngày mai" : $"Sắp đến hạn (còn {d} ngày)", up));
                break;
            case UrgencyLevel.ThisWeek:
                up = 16;
                reasons.Add(new PriorityReason("Hạn trong vòng 7 ngày", up));
                break;
            default:
                var weeks = (t.DueAt.Date - now.Date).Days / 7;
                up = Math.Max(0, 12 - 2 * weeks);
                reasons.Add(new PriorityReason("Còn nhiều thời gian", up));
                break;
        }
        score += up;

        // 3. Áp lực thời gian: thời lượng còn lại so với giờ làm việc còn lại đến hạn
        bool assumed = !t.EstimatedHours.HasValue || t.EstimatedHours <= 0;
        double estimate = assumed ? _settings.DefaultEstimateHours : t.EstimatedHours!.Value;
        double required = Math.Max(0, estimate * (100 - Math.Clamp(t.Progress, 0, 100)) / 100.0);
        double remainingWork = urgency == UrgencyLevel.Overdue ? 0 : _calendar.WorkingHoursBetween(now, t.DueAt);
        bool timeRisk = false;
        if (urgency != UrgencyLevel.Overdue && required > 0)
        {
            double ratio = remainingWork <= 0 ? double.PositiveInfinity : required / remainingWork;
            if (ratio >= 1)
            {
                timeRisk = true;
                double p = assumed ? 8 : 15;
                score += p;
                reasons.Add(new PriorityReason(assumed
                    ? $"Có nguy cơ không đủ thời gian (theo thời lượng ước tính mặc định {estimate:0.#} giờ, còn {remainingWork:0.#} giờ làm việc)"
                    : $"Không đủ giờ làm việc còn lại: cần {required:0.#} giờ, chỉ còn {remainingWork:0.#} giờ", p));
            }
            else if (ratio >= 0.5)
            {
                double p = assumed ? 4 : 8;
                score += p;
                reasons.Add(new PriorityReason(assumed
                    ? $"Thời gian còn lại khá sát (ước tính mặc định {estimate:0.#} giờ)"
                    : $"Thời gian sát: cần {required:0.#}/{remainingWork:0.#} giờ làm việc còn lại", p));
            }
        }

        // 4. Thời điểm thực hiện cố định trong hôm nay
        if (t.IsFixedTime && t.PlannedStart.HasValue && t.PlannedStart.Value.Date == now.Date && t.PlannedStart.Value >= now.AddHours(-1))
        {
            score += 20;
            reasons.Add(new PriorityReason($"Có lịch cố định hôm nay lúc {t.PlannedStart:HH:mm}", 20));
        }

        // 5. Công việc phụ thuộc chưa xong: chưa thể làm, xếp sau
        if (t.IsBlocked)
        {
            score -= 25;
            reasons.Add(new PriorityReason($"Phải chờ {string.Join(", ", t.BlockedBy)} hoàn thành trước", -25));
        }

        // 6. Trạng thái
        if (t.Status == WorkTaskStatus.InProgress)
        {
            score += 3;
            reasons.Add(new PriorityReason("Đang thực hiện dở", 3));
        }
        else if (t.Status == WorkTaskStatus.NeedMoreDocuments)
        {
            score += 5;
            reasons.Add(new PriorityReason("Cần bổ sung hồ sơ", 5));
        }

        bool waiting = StatusGroups.IsWaiting(t.Status);
        if (waiting)
        {
            if (t.ExpectedResponseAt.HasValue && t.ExpectedResponseAt.Value <= now)
                reasons.Add(new PriorityReason("Đã đến/qua ngày dự kiến phản hồi — cần đôn đốc", 0));
            else
                reasons.Add(new PriorityReason("Đang chờ đơn vị khác — theo dõi, đôn đốc", 0));
        }

        bool important = t.Priority >= TaskPriority.High;
        bool urgent = urgency == UrgencyLevel.Overdue
                      || (t.DueAt - now).TotalDays <= 2
                      || (timeRisk && !assumed);
        var quadrant = (important, urgent) switch
        {
            (true, true) => EisenhowerQuadrant.DoNow,
            (true, false) => EisenhowerQuadrant.Schedule,
            (false, true) => EisenhowerQuadrant.QuickHandle,
            _ => EisenhowerQuadrant.Later
        };

        return new PriorityAssessment
        {
            Task = t,
            Score = score,
            Urgency = urgency,
            Quadrant = quadrant,
            SuggestedPriority = SuggestLevel(score),
            Reasons = reasons,
            RemainingWorkHours = remainingWork,
            RequiredHours = required,
            RequiredHoursIsAssumed = assumed,
            TimeRisk = timeRisk,
            IsWaiting = waiting,
            IsOnHold = t.Status == WorkTaskStatus.OnHold
        };
    }

    public static TaskPriority SuggestLevel(double score) => score switch
    {
        >= 80 => TaskPriority.Urgent,
        >= 60 => TaskPriority.High,
        >= 35 => TaskPriority.Normal,
        _ => TaskPriority.Low
    };

    /// <summary>
    /// Sắp xếp các công việc đang trực tiếp xử lý theo thứ tự đề xuất.
    /// Công việc chờ phản hồi/phối hợp/phê duyệt, tạm dừng hoặc đã đóng bị loại khỏi danh sách này.
    /// </summary>
    public List<PriorityAssessment> RankDirectWork(IEnumerable<WorkTask> tasks, DateTime now) =>
        tasks.Where(t => !t.IsDeleted && StatusGroups.IsDirectWork(t.Status))
             .Select(t => Assess(t, now))
             .OrderByDescending(a => a.Score)
             .ThenBy(a => a.Task.DueAt)
             .ThenByDescending(a => a.Task.Priority)
             .ThenBy(a => a.Task.Id)
             .ToList();

    /// <summary>Danh sách công việc chờ, sắp theo ngày cần đôn đốc (ngày dự kiến phản hồi, nếu không có thì theo hạn).</summary>
    public List<PriorityAssessment> RankWaiting(IEnumerable<WorkTask> tasks, DateTime now) =>
        tasks.Where(t => !t.IsDeleted && StatusGroups.IsWaiting(t.Status))
             .Select(t => Assess(t, now))
             .OrderBy(a => a.Task.ExpectedResponseAt ?? a.Task.DueAt)
             .ThenByDescending(a => a.Score)
             .ToList();
}
