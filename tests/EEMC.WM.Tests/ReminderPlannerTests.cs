using EEMC.WM.Core.Domain;
using EEMC.WM.Core.Services;
using EEMC.WM.Core.Settings;
using EEMC.WM.Core.Text;

namespace EEMC.WM.Tests;

public class ReminderPlannerTests
{
    private static readonly DateTime Created = new(2026, 10, 1, 8, 0, 0);
    private static readonly DateTime Due = new(2026, 10, 20, 17, 0, 0);
    private readonly WorkSettings _s = new();

    private static WorkTask Task() => new()
    {
        Id = 1, Code = "CV-2026-00001", Title = "Báo cáo tiến độ", CreatedAt = Created, ReceivedAt = Created, DueAt = Due,
        Priority = TaskPriority.High
    };

    /// <summary>Mô phỏng chạy kiểm tra liên tục và ghi nhận khóa như ReminderService.</summary>
    private List<ReminderMessage> Simulate(WorkTask t, IEnumerable<DateTime> checks, HashSet<string>? fired = null)
    {
        fired ??= new HashSet<string>();
        var messages = new List<ReminderMessage>();
        foreach (var now in checks)
        {
            var d = ReminderPlanner.Decide(t, _s, now, fired);
            foreach (var k in d.KeysToLog) fired.Add(k);
            if (d.Message != null) messages.Add(d.Message);
        }
        return messages;
    }

    private static IEnumerable<DateTime> EveryMinutes(DateTime from, DateTime to, int step)
    {
        for (var t = from; t <= to; t = t.AddMinutes(step)) yield return t;
    }

    [Fact]
    public void Each_threshold_fires_exactly_once_when_checked_continuously()
    {
        var msgs = Simulate(Task(), EveryMinutes(Due.AddDays(-4), Due.AddMinutes(30), 5));
        // 3 ngày, 1 ngày, 4 giờ, 1 giờ, đến hạn
        Assert.Equal(5, msgs.Count);
        Assert.Equal("Công việc đến hạn", msgs[^1].Title);
    }

    [Fact]
    public void After_long_downtime_only_most_relevant_reminder_is_shown()
    {
        // Ứng dụng không chạy từ trước 3 ngày, mở lại khi còn 2 giờ
        var msgs = Simulate(Task(), new[] { Due.AddHours(-2), Due.AddHours(-2).AddMinutes(1) });
        var m = Assert.Single(msgs);
        Assert.Contains("4 giờ", m.Title);
    }

    [Fact]
    public void Opening_app_after_deadline_shows_overdue_once_per_period()
    {
        var t = Task();
        var fired = new HashSet<string>();
        var first = Simulate(t, new[] { Due.AddHours(5), Due.AddHours(6) }, fired);
        Assert.Single(first);
        Assert.Contains("quá hạn", first[0].Title);
        var nextDay = Simulate(t, new[] { Due.AddHours(30) }, fired);
        Assert.Single(nextDay);
    }

    [Fact]
    public void Thresholds_passed_before_creation_are_skipped()
    {
        var t = Task();
        t.CreatedAt = Due.AddHours(-30); // tạo khi còn 30 giờ: mốc 3 ngày đã qua
        var msgs = Simulate(t, new[] { t.CreatedAt.AddMinutes(1) });
        Assert.Empty(msgs);
        var later = Simulate(t, new[] { Due.AddHours(-23) });
        Assert.Single(later); // mốc 1 ngày vẫn nhắc
    }

    [Fact]
    public void Closed_or_deleted_tasks_are_not_reminded()
    {
        var t = Task();
        t.Status = WorkTaskStatus.Completed;
        Assert.Empty(Simulate(t, new[] { Due.AddHours(2) }));
        t.Status = WorkTaskStatus.InProgress;
        t.IsDeleted = true;
        Assert.Empty(Simulate(t, new[] { Due.AddHours(2) }));
    }

    [Fact]
    public void Snooze_suppresses_then_reminds_again_once()
    {
        var t = Task();
        var fired = new HashSet<string>();
        Assert.Single(Simulate(t, new[] { Due.AddHours(-3) }, fired));
        t.SnoozeUntil = Due.AddHours(-3).AddMinutes(15);
        Assert.Empty(Simulate(t, new[] { Due.AddHours(-3).AddMinutes(10) }, fired));
        var after = Simulate(t, new[] { Due.AddHours(-3).AddMinutes(16), Due.AddHours(-3).AddMinutes(20) }, fired);
        var m = Assert.Single(after);
        Assert.Contains("tạm hoãn", m.Title);
    }

    [Fact]
    public void Waiting_task_gets_follow_up_once_a_day_after_expected_response()
    {
        var t = Task();
        t.DueAt = Due.AddDays(30);
        t.Status = WorkTaskStatus.WaitingCoordinator;
        t.ExpectedResponseAt = new DateTime(2026, 10, 10, 9, 0, 0);
        t.Coordinator = "Phòng Kỹ thuật";
        var day1 = Simulate(t, EveryMinutes(new DateTime(2026, 10, 10, 9, 0, 0), new DateTime(2026, 10, 10, 17, 0, 0), 30));
        var m = Assert.Single(day1);
        Assert.Contains("đôn đốc", m.Title);
        Assert.Contains("Phòng Kỹ thuật", m.Message);
    }

    [Fact]
    public void Disabled_offsets_are_ignored()
    {
        foreach (var o in _s.ReminderOffsets) o.Enabled = o.Minutes == 60;
        var msgs = Simulate(Task(), EveryMinutes(Due.AddDays(-4), Due.AddMinutes(-1), 10));
        var m = Assert.Single(msgs);
        Assert.Contains("1 giờ", m.Title);
    }

    [Theory]
    [InlineData("Báo cáo TIẾN ĐỘ đề tài Kháng điện", "bao cao tien do de tai khang dien")]
    [InlineData("  Đăng   ký  DN KH&CN ", "dang ky dn kh&cn")]
    public void Normalizer_removes_diacritics_and_case(string input, string expected)
    {
        Assert.Equal(expected, TextNormalizer.Normalize(input));
    }

    [Theory]
    [InlineData(90, "Còn 1 giờ 30 phút")]
    [InlineData(-60 * 26, "Quá hạn 1 ngày 2 giờ")]
    [InlineData(30, "Còn 30 phút")]
    public void Time_remaining_text(int minutes, string expected)
    {
        var now = new DateTime(2026, 10, 19, 9, 0, 0);
        Assert.Equal(expected, Labels.TimeRemaining(now.AddMinutes(minutes), now));
    }
}
