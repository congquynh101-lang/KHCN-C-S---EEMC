using EEMC.WM.Core.Settings;

namespace EEMC.WM.Core.Services;

public readonly record struct TimeSlot(DateTime Start, DateTime End)
{
    public double Hours => (End - Start).TotalHours;
    public bool Overlaps(TimeSlot other) => Start < other.End && other.Start < End;
}

/// <summary>Tính toán theo giờ làm việc, ngày làm việc và ngày nghỉ của người dùng.</summary>
public sealed class WorkCalendar
{
    private readonly WorkSettings _settings;
    private readonly List<WorkPeriod> _periods;

    public WorkCalendar(WorkSettings settings)
    {
        _settings = settings;
        _periods = settings.WorkPeriods.Where(p => p.End > p.Start).OrderBy(p => p.Start).ToList();
    }

    public bool IsWorkDay(DateTime date) => _settings.IsWorkDay(date);

    /// <summary>Các khoảng giờ làm việc nằm trong [from, to].</summary>
    public IEnumerable<TimeSlot> WorkSlots(DateTime from, DateTime to)
    {
        if (to <= from || _periods.Count == 0) yield break;
        for (var day = from.Date; day <= to.Date; day = day.AddDays(1))
        {
            if (!IsWorkDay(day)) continue;
            foreach (var p in _periods)
            {
                var s = day + p.Start;
                var e = day + p.End;
                if (s < from) s = from;
                if (e > to) e = to;
                if (e > s) yield return new TimeSlot(s, e);
            }
        }
    }

    public double WorkingHoursBetween(DateTime from, DateTime to) =>
        WorkSlots(from, to).Sum(s => s.Hours);

    /// <summary>Số ngày làm việc từ hôm nay (không tính hôm nay) đến ngày đích (tính cả ngày đích).</summary>
    public int WorkDaysUntil(DateTime today, DateTime target)
    {
        int n = 0;
        for (var d = today.Date.AddDays(1); d <= target.Date; d = d.AddDays(1))
            if (IsWorkDay(d)) n++;
        return n;
    }

    /// <summary>Thời điểm làm việc sớm nhất kể từ <paramref name="from"/>.</summary>
    public DateTime? NextWorkMoment(DateTime from, int maxDays = 60)
    {
        var slot = WorkSlots(from, from.AddDays(maxDays)).FirstOrDefault();
        return slot.End > slot.Start ? slot.Start : null;
    }
}
