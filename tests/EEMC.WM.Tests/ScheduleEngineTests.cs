using EEMC.WM.Core.Domain;
using EEMC.WM.Core.Services;
using EEMC.WM.Core.Settings;

namespace EEMC.WM.Tests;

public class ScheduleEngineTests
{
    // Thứ Hai 19/10/2026 08:00
    private static readonly DateTime Monday = new(2026, 10, 19, 8, 0, 0);
    private readonly WorkSettings _settings = new();

    private static WorkTask T(int id, DateTime due, TaskPriority p, double? est) =>
        new() { Id = id, Code = $"CV-{id}", Title = $"T{id}", DueAt = due, ReceivedAt = Monday.AddDays(-1), CreatedAt = Monday.AddDays(-1), Priority = p, EstimatedHours = est };

    [Fact]
    public void Work_calendar_counts_only_working_hours_and_days()
    {
        var cal = new WorkCalendar(_settings);
        Assert.Equal(8, cal.WorkingHoursBetween(Monday, Monday.AddHours(10)), 3);
        // Thứ Sáu 16:00 → Thứ Hai 09:00 = 1 giờ thứ Sáu + 1 giờ thứ Hai
        var fri = new DateTime(2026, 10, 23, 16, 0, 0);
        Assert.Equal(2, cal.WorkingHoursBetween(fri, fri.AddDays(3).AddHours(-7)), 3);
    }

    [Fact]
    public void Blocks_are_inside_working_hours_and_skip_lunch_and_weekend()
    {
        var t = T(1, Monday.AddDays(10), TaskPriority.Normal, 10);
        var p = new ScheduleEngine(_settings).Propose(new[] { t }, Monday);
        Assert.Equal(10, p.Blocks.Sum(b => b.Hours), 3);
        var cal = new WorkCalendar(_settings);
        foreach (var b in p.Blocks)
        {
            Assert.True(cal.IsWorkDay(b.Start));
            Assert.True(b.Start.TimeOfDay >= TimeSpan.FromHours(8));
            Assert.True(b.End.TimeOfDay <= TimeSpan.FromHours(17));
            Assert.False(b.Start.TimeOfDay < TimeSpan.FromHours(13) && b.End.TimeOfDay > TimeSpan.FromHours(12));
        }
        // 8 giờ thứ Hai + 2 giờ thứ Ba → xong 10:00 thứ Ba
        Assert.Equal(Monday.AddDays(1).AddHours(2), p.FinishTimes[1]);
    }

    [Fact]
    public void Higher_priority_gets_earlier_slot()
    {
        var low = T(1, Monday.AddDays(4), TaskPriority.Low, 2);
        var urgent = T(2, Monday.AddDays(4), TaskPriority.Urgent, 2);
        var p = new ScheduleEngine(_settings).Propose(new[] { low, urgent }, Monday);
        Assert.Equal(2, p.Blocks.First().Task.Id);
        Assert.Equal(Monday, p.Blocks.First().Start);
    }

    [Fact]
    public void Fixed_meetings_are_kept_and_avoided()
    {
        var meeting = T(1, Monday.AddHours(3), TaskPriority.Normal, 2);
        meeting.IsFixedTime = true;
        meeting.PlannedStart = Monday.AddHours(1); // 09:00–11:00
        var work = T(2, Monday.AddDays(3), TaskPriority.High, 3);

        var p = new ScheduleEngine(_settings).Propose(new[] { meeting, work }, Monday);
        var fixedBlock = Assert.Single(p.Blocks, b => b.IsFixed);
        Assert.Equal(Monday.AddHours(1), fixedBlock.Start);
        foreach (var b in p.Blocks.Where(b => !b.IsFixed))
            Assert.False(b.Start < fixedBlock.End && fixedBlock.Start < b.End, "Trùng lịch họp cố định");
        Assert.Equal(3, p.Blocks.Where(b => b.Task.Id == 2).Sum(b => b.Hours), 3);
    }

    [Fact]
    public void Warns_certainly_when_known_estimate_cannot_meet_deadline()
    {
        var t = T(1, Monday.AddHours(6), TaskPriority.High, 8); // hạn 14:00, chỉ có 5 giờ làm việc
        var p = new ScheduleEngine(_settings).Propose(new[] { t }, Monday);
        var w = Assert.Single(p.Warnings);
        Assert.True(w.IsCertain);
        Assert.Contains("Không đủ thời gian", w.Message);
    }

    [Fact]
    public void Assumed_estimate_only_produces_risk_warning_and_marks_blocks_as_estimate()
    {
        var t = T(1, Monday.AddHours(1), TaskPriority.High, null); // mặc định 2 giờ, hạn 09:00
        var p = new ScheduleEngine(_settings).Propose(new[] { t }, Monday);
        Assert.All(p.Blocks, b => Assert.True(b.IsEstimate));
        var w = Assert.Single(p.Warnings);
        Assert.False(w.IsCertain);
        Assert.Contains("nguy cơ", w.Message);
    }

    [Fact]
    public void Planned_start_is_respected()
    {
        var t = T(1, Monday.AddDays(3), TaskPriority.Urgent, 1);
        t.PlannedStart = Monday.AddDays(1).AddHours(6); // Thứ Ba 14:00
        var p = new ScheduleEngine(_settings).Propose(new[] { t }, Monday);
        Assert.Equal(Monday.AddDays(1).AddHours(6), p.Blocks.Single().Start);
    }

    [Fact]
    public void Waiting_and_closed_tasks_are_not_scheduled()
    {
        var w = T(1, Monday.AddDays(2), TaskPriority.Urgent, 2);
        w.Status = WorkTaskStatus.WaitingApproval;
        var c = T(2, Monday.AddDays(2), TaskPriority.Urgent, 2);
        c.Status = WorkTaskStatus.Completed;
        var p = new ScheduleEngine(_settings).Propose(new[] { w, c }, Monday);
        Assert.Empty(p.Blocks);
    }

    [Fact]
    public void Custom_work_hours_are_used()
    {
        _settings.WorkPeriods = new() { new WorkPeriod(TimeSpan.FromHours(7.5), TimeSpan.FromHours(11.5)) };
        _settings.WorkDays.Add(DayOfWeek.Saturday);
        var cal = new WorkCalendar(_settings);
        var sat = new DateTime(2026, 10, 24);
        Assert.Equal(4, cal.WorkingHoursBetween(sat, sat.AddDays(1)), 3);
    }

    [Fact]
    public void Holidays_are_skipped()
    {
        _settings.Holidays.Add(Monday.Date);
        var cal = new WorkCalendar(_settings);
        Assert.Equal(0, cal.WorkingHoursBetween(Monday.Date, Monday.Date.AddDays(1)), 3);
    }
}
