namespace EEMC.WM.Core.Settings;

public class WorkPeriod
{
    public TimeSpan Start { get; set; }
    public TimeSpan End { get; set; }

    public WorkPeriod() { }
    public WorkPeriod(TimeSpan start, TimeSpan end) { Start = start; End = end; }

    public double Hours => Math.Max(0, (End - Start).TotalHours);
}

/// <summary>Một mốc nhắc trước hạn, ví dụ 3 ngày, 1 ngày, 4 giờ, 1 giờ, 0 (khi đến hạn).</summary>
public class ReminderOffset
{
    public int Minutes { get; set; }
    public bool Enabled { get; set; } = true;

    public ReminderOffset() { }
    public ReminderOffset(int minutes, bool enabled = true) { Minutes = minutes; Enabled = enabled; }

    public string Key => $"before-{Minutes}";

    public string Label => Minutes switch
    {
        0 => "Khi đến hạn",
        _ when Minutes % 1440 == 0 => $"Trước hạn {Minutes / 1440} ngày",
        _ when Minutes % 60 == 0 => $"Trước hạn {Minutes / 60} giờ",
        _ => $"Trước hạn {Minutes} phút"
    };
}

/// <summary>Toàn bộ cấu hình người dùng có thể chỉnh sửa.</summary>
public class WorkSettings
{
    public List<WorkPeriod> WorkPeriods { get; set; } = new()
    {
        new WorkPeriod(new TimeSpan(8, 0, 0), new TimeSpan(12, 0, 0)),
        new WorkPeriod(new TimeSpan(13, 0, 0), new TimeSpan(17, 0, 0)),
    };

    public List<DayOfWeek> WorkDays { get; set; } = new()
    {
        DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday
    };

    /// <summary>Ngày nghỉ lễ / nghỉ cá nhân (chỉ phần ngày).</summary>
    public List<DateTime> Holidays { get; set; } = new();

    public List<ReminderOffset> ReminderOffsets { get; set; } = new()
    {
        new ReminderOffset(3 * 1440),
        new ReminderOffset(1440),
        new ReminderOffset(240),
        new ReminderOffset(60),
        new ReminderOffset(0),
    };

    public bool RemindWhenOverdue { get; set; } = true;
    /// <summary>Nhắc lại công việc quá hạn mỗi N giờ (mặc định 1 lần/ngày làm việc).</summary>
    public int OverdueRepeatHours { get; set; } = 24;
    public bool RemindFollowUp { get; set; } = true;
    public bool WindowsNotification { get; set; } = true;

    /// <summary>Thời lượng mặc định dùng để GỢI Ý khi chưa có thời lượng dự kiến (luôn đánh dấu là ước tính).</summary>
    public double DefaultEstimateHours { get; set; } = 2;

    /// <summary>Số ngày được coi là "sắp đến hạn".</summary>
    public int DueSoonDays { get; set; } = 3;

    public bool DarkTheme { get; set; }
    public bool MinimizeToTray { get; set; } = true;

    public string? BackupFolder { get; set; }
    public bool AutoBackupDaily { get; set; } = true;
    public int BackupKeepCount { get; set; } = 30;

    // ---- Báo cáo ----
    /// <summary>Đơn vị lập báo cáo (dòng thứ hai của khối tiêu đề).</summary>
    public string ReportUnit { get; set; } = "THƯ KÝ KH&CN";
    public string ReportAuthor { get; set; } = "";
    public string ReportPlace { get; set; } = "Hà Nội";

    // ---- Đồng bộ (thiết lập riêng từng máy, không đồng bộ) ----
    public bool SyncEnabled { get; set; }
    /// <summary>Thư mục dùng chung (OneDrive/SharePoint của EEMC) chứa dữ liệu đồng bộ.</summary>
    public string? SyncFolder { get; set; }
    public int SyncIntervalMinutes { get; set; } = 2;

    /// <summary>Sao chép các thiết lập làm việc dùng chung (được đồng bộ giữa các máy).</summary>
    public void CopySharedFrom(WorkSettings o)
    {
        WorkPeriods = o.WorkPeriods.Select(p => new WorkPeriod(p.Start, p.End)).ToList();
        WorkDays = o.WorkDays.ToList();
        Holidays = o.Holidays.ToList();
        ReminderOffsets = o.ReminderOffsets.Select(r => new ReminderOffset(r.Minutes, r.Enabled)).ToList();
        RemindWhenOverdue = o.RemindWhenOverdue;
        OverdueRepeatHours = o.OverdueRepeatHours;
        RemindFollowUp = o.RemindFollowUp;
        DefaultEstimateHours = o.DefaultEstimateHours;
        DueSoonDays = o.DueSoonDays;
    }

    /// <summary>Chuỗi so sánh các thiết lập dùng chung (để biết có thay đổi cần đồng bộ không).</summary>
    public string SharedFingerprint() =>
        string.Join("|",
            string.Join(",", WorkPeriods.Select(p => $"{p.Start}-{p.End}")),
            string.Join(",", WorkDays.OrderBy(d => d)),
            string.Join(",", Holidays.Select(h => h.ToString("yyyyMMdd")).OrderBy(x => x)),
            string.Join(",", ReminderOffsets.Select(r => $"{r.Minutes}:{r.Enabled}")),
            RemindWhenOverdue, OverdueRepeatHours, RemindFollowUp, DefaultEstimateHours, DueSoonDays);

    public bool IsWorkDay(DateTime date) =>
        WorkDays.Contains(date.DayOfWeek) && !Holidays.Any(h => h.Date == date.Date);

    public double HoursPerWorkDay => WorkPeriods.Sum(p => p.Hours);

    public IEnumerable<string> Validate()
    {
        if (WorkPeriods.Count == 0) yield return "Cần ít nhất một khung giờ làm việc.";
        foreach (var p in WorkPeriods)
            if (p.End <= p.Start) yield return $"Khung giờ {p.Start:hh\\:mm}–{p.End:hh\\:mm} không hợp lệ.";
        var ordered = WorkPeriods.OrderBy(p => p.Start).ToList();
        for (int i = 1; i < ordered.Count; i++)
            if (ordered[i].Start < ordered[i - 1].End) yield return "Các khung giờ làm việc bị chồng lấn.";
        if (WorkDays.Count == 0) yield return "Cần chọn ít nhất một ngày làm việc trong tuần.";
        if (DefaultEstimateHours <= 0 || DefaultEstimateHours > 200) yield return "Thời lượng mặc định phải trong khoảng 0–200 giờ.";
        if (OverdueRepeatHours < 1) yield return "Chu kỳ nhắc quá hạn tối thiểu 1 giờ.";
        if (BackupKeepCount < 1) yield return "Số bản sao lưu giữ lại tối thiểu là 1.";
    }
}
