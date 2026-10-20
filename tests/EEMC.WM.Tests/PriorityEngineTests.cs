using EEMC.WM.Core.Domain;
using EEMC.WM.Core.Services;
using EEMC.WM.Core.Settings;

namespace EEMC.WM.Tests;

public class PriorityEngineTests
{
    // Thứ Hai 19/10/2026 09:00
    private static readonly DateTime Now = new(2026, 10, 19, 9, 0, 0);
    private readonly PriorityEngine _engine = new(new WorkSettings());

    private static WorkTask T(int id, string title, DateTime due, TaskPriority p, WorkTaskStatus s = WorkTaskStatus.NotStarted, double? est = null) =>
        new() { Id = id, Code = $"CV-{id}", Title = title, DueAt = due, ReceivedAt = Now.AddDays(-10), CreatedAt = Now.AddDays(-10), Priority = p, Status = s, EstimatedHours = est };

    [Fact]
    public void Example_from_requirements_orders_overdue_then_tomorrow_then_next_week()
    {
        var a = T(1, "A", Now.Date.AddDays(1).AddHours(17), TaskPriority.High);
        var b = T(2, "B", Now.Date.AddDays(7).AddHours(17), TaskPriority.Normal);
        var c = T(3, "C", Now.AddDays(-2), TaskPriority.Low);

        var ranked = _engine.RankDirectWork(new[] { b, a, c }, Now);

        Assert.Equal(new[] { "C", "A", "B" }, ranked.Select(r => r.Task.Title));
        Assert.Contains(ranked[0].Reasons, r => r.Text.Contains("quá hạn", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(ranked[1].Reasons, r => r.Text.Contains("ngày mai"));
        Assert.All(ranked, r => Assert.False(string.IsNullOrWhiteSpace(r.Explanation)));
    }

    [Fact]
    public void Never_changes_user_priority_but_can_suggest_different_level()
    {
        var c = T(3, "C", Now.AddDays(-2), TaskPriority.Low);
        var a = _engine.Assess(c, Now);
        Assert.Equal(TaskPriority.Low, c.Priority);
        Assert.Equal(TaskPriority.Low, a.Task.Priority);
        Assert.True(a.SuggestedPriority > TaskPriority.Low);
        Assert.True(a.SuggestionDiffers);
    }

    [Fact]
    public void Waiting_and_on_hold_tasks_are_excluded_from_direct_work_but_ranked_for_follow_up()
    {
        var w = T(1, "Chờ phản hồi", Now.AddDays(1), TaskPriority.Urgent, WorkTaskStatus.WaitingResponse);
        w.ExpectedResponseAt = Now.AddHours(-1);
        var h = T(2, "Tạm dừng", Now.AddDays(1), TaskPriority.Urgent, WorkTaskStatus.OnHold);
        var d = T(3, "Đang làm", Now.AddDays(5), TaskPriority.Low, WorkTaskStatus.InProgress);
        var done = T(4, "Xong", Now.AddDays(1), TaskPriority.Urgent, WorkTaskStatus.Completed);

        var direct = _engine.RankDirectWork(new[] { w, h, d, done }, Now);
        Assert.Single(direct);
        Assert.Equal("Đang làm", direct[0].Task.Title);

        var waiting = _engine.RankWaiting(new[] { w, h, d, done }, Now);
        Assert.Single(waiting);
        Assert.Contains(waiting[0].Reasons, r => r.Text.Contains("đôn đốc"));
    }

    [Theory]
    [InlineData(-1, UrgencyLevel.Overdue)]
    [InlineData(3, UrgencyLevel.DueToday)]      // 12:00 hôm nay
    [InlineData(24, UrgencyLevel.DueSoon)]
    [InlineData(24 * 5, UrgencyLevel.ThisWeek)]
    [InlineData(24 * 20, UrgencyLevel.Later)]
    public void Urgency_is_derived_from_real_deadline(int hoursFromNow, UrgencyLevel expected)
    {
        Assert.Equal(expected, _engine.GetUrgency(Now.AddHours(hoursFromNow), Now));
    }

    [Fact]
    public void Known_estimate_exceeding_remaining_work_hours_is_a_certain_time_risk()
    {
        // Hạn 17:00 hôm nay, còn 7 giờ làm việc (09–12, 13–17), cần 10 giờ
        var t = T(1, "Báo cáo", Now.Date.AddHours(17), TaskPriority.Normal, est: 10);
        var a = _engine.Assess(t, Now);
        Assert.True(a.TimeRisk);
        Assert.False(a.RequiredHoursIsAssumed);
        Assert.Equal(7, a.RemainingWorkHours, 3);
        Assert.Contains(a.Reasons, r => r.Text.StartsWith("Không đủ giờ làm việc"));
    }

    [Fact]
    public void Missing_estimate_is_flagged_as_assumed_and_never_asserted()
    {
        var t = T(1, "Soạn tờ trình", Now.AddHours(1), TaskPriority.Normal);
        var a = _engine.Assess(t, Now);
        Assert.True(a.RequiredHoursIsAssumed);
        Assert.True(a.TimeRisk);
        Assert.Contains(a.Reasons, r => r.Text.Contains("ước tính mặc định"));
        Assert.DoesNotContain(a.Reasons, r => r.Text.StartsWith("Không đủ giờ làm việc"));
    }

    [Fact]
    public void Progress_reduces_required_hours()
    {
        var t = T(1, "X", Now.AddDays(3), TaskPriority.Normal, est: 8);
        t.Progress = 75;
        Assert.Equal(2, _engine.Assess(t, Now).RequiredHours, 3);
    }

    [Fact]
    public void Fixed_time_today_gets_boost()
    {
        var meeting = T(1, "Họp Hội đồng", Now.Date.AddDays(10), TaskPriority.Normal);
        meeting.IsFixedTime = true;
        meeting.PlannedStart = Now.Date.AddHours(14);
        var other = T(2, "Khác", Now.Date.AddDays(10), TaskPriority.Normal);
        var ranked = _engine.RankDirectWork(new[] { other, meeting }, Now);
        Assert.Equal("Họp Hội đồng", ranked[0].Task.Title);
    }

    [Fact]
    public void Eisenhower_quadrants()
    {
        Assert.Equal(EisenhowerQuadrant.DoNow, _engine.Assess(T(1, "a", Now.AddDays(1), TaskPriority.High), Now).Quadrant);
        Assert.Equal(EisenhowerQuadrant.Schedule, _engine.Assess(T(2, "b", Now.AddDays(10), TaskPriority.Urgent), Now).Quadrant);
        Assert.Equal(EisenhowerQuadrant.QuickHandle, _engine.Assess(T(3, "c", Now.AddHours(5), TaskPriority.Low), Now).Quadrant);
        Assert.Equal(EisenhowerQuadrant.Later, _engine.Assess(T(4, "d", Now.AddDays(20), TaskPriority.Normal), Now).Quadrant);
    }

    [Fact]
    public void Higher_user_priority_wins_when_deadline_equal()
    {
        var due = Now.AddDays(2);
        var ranked = _engine.RankDirectWork(new[]
        {
            T(1, "thấp", due, TaskPriority.Low), T(2, "khẩn", due, TaskPriority.Urgent), T(3, "cao", due, TaskPriority.High)
        }, Now);
        Assert.Equal(new[] { "khẩn", "cao", "thấp" }, ranked.Select(r => r.Task.Title));
    }
}
