using EEMC.WM.Core.Domain;
using EEMC.WM.Core.Settings;
using EEMC.WM.Core.Text;

namespace EEMC.WM.Core.Services;

public sealed class ScheduledBlock
{
    public required WorkTask Task { get; init; }
    public DateTime Start { get; init; }
    public DateTime End { get; init; }
    /// <summary>Lịch cố định (họp...), không di chuyển.</summary>
    public bool IsFixed { get; init; }
    /// <summary>Thời lượng là ước tính mặc định vì công việc chưa có thời lượng dự kiến.</summary>
    public bool IsEstimate { get; init; }
    public double Hours => (End - Start).TotalHours;
}

public sealed class ScheduleWarning
{
    public required WorkTask Task { get; init; }
    public string Message { get; init; } = "";
    /// <summary>True: chắc chắn không đủ thời gian (đã có thời lượng dự kiến). False: chỉ là nguy cơ theo ước tính mặc định.</summary>
    public bool IsCertain { get; init; }
}

public sealed class ScheduleProposal
{
    public List<ScheduledBlock> Blocks { get; } = new();
    public List<ScheduleWarning> Warnings { get; } = new();

    /// <summary>Thời điểm kết thúc dự kiến của từng công việc theo lịch đề xuất.</summary>
    public Dictionary<int, DateTime> FinishTimes { get; } = new();

    public IEnumerable<ScheduledBlock> BlocksOn(DateTime day) =>
        Blocks.Where(b => b.Start.Date == day.Date).OrderBy(b => b.Start);
}

/// <summary>
/// Lập lịch đề xuất: xếp các công việc theo thứ tự ưu tiên vào các khoảng giờ làm việc còn trống,
/// tránh lịch cố định, cảnh báo công việc không đủ thời gian. Đây là lịch ĐỀ XUẤT, người dùng tự quyết định.
/// </summary>
public sealed class ScheduleEngine
{
    private const double MinChunkHours = 0.25;
    private readonly WorkSettings _settings;
    private readonly WorkCalendar _calendar;
    private readonly PriorityEngine _priority;

    public ScheduleEngine(WorkSettings settings)
    {
        _settings = settings;
        _calendar = new WorkCalendar(settings);
        _priority = new PriorityEngine(settings);
    }

    /// <param name="tasks">Tất cả công việc chưa đóng.</param>
    /// <param name="now">Thời điểm bắt đầu lập lịch.</param>
    /// <param name="horizonDays">Số ngày lịch tối đa để xếp.</param>
    public ScheduleProposal Propose(IEnumerable<WorkTask> tasks, DateTime now, int horizonDays = 30)
    {
        var proposal = new ScheduleProposal();
        var list = tasks.Where(t => !t.IsDeleted && !StatusGroups.IsClosed(t.Status)).ToList();
        var horizonEnd = now.Date.AddDays(horizonDays + 1);

        // 1. Lịch cố định chiếm chỗ trước
        var busy = new List<TimeSlot>();
        foreach (var f in list.Where(t => t.IsFixedTime && t.PlannedStart.HasValue && t.PlannedStart.Value < horizonEnd))
        {
            var start = f.PlannedStart!.Value;
            var hours = f.EstimatedHours is > 0 ? f.EstimatedHours.Value : 1.0;
            var end = start.AddHours(hours);
            if (end <= now) continue;
            busy.Add(new TimeSlot(start, end));
            proposal.Blocks.Add(new ScheduledBlock
            {
                Task = f, Start = start, End = end, IsFixed = true, IsEstimate = !(f.EstimatedHours is > 0)
            });
            proposal.FinishTimes[f.Id] = end;
        }

        // 2. Khoảng trống làm việc
        var free = SubtractBusy(_calendar.WorkSlots(now, horizonEnd), busy).ToList();

        // 3. Xếp công việc trực tiếp xử lý theo thứ tự ưu tiên; công việc phụ thuộc xếp sau khi công việc trước xong
        var ranked = _priority.RankDirectWork(list.Where(t => !t.IsFixedTime), now);
        var openIds = list.Where(t => !StatusGroups.IsClosed(t.Status)).Select(t => t.Id).ToHashSet();
        var order = new List<PriorityAssessment>();
        var placed = new HashSet<int>();
        var pending = ranked.ToList();
        bool progress = true;
        while (pending.Count > 0 && progress)
        {
            progress = false;
            foreach (var a in pending.ToList())
            {
                // Chỉ chờ những công việc trước còn mở và có trong danh sách xếp lịch
                var waitFor = a.Task.DependsOnIds.Where(id => openIds.Contains(id) && ranked.Any(r => r.Task.Id == id));
                if (waitFor.All(placed.Contains))
                {
                    order.Add(a);
                    placed.Add(a.Task.Id);
                    pending.Remove(a);
                    progress = true;
                }
            }
        }
        foreach (var a in pending)
            proposal.Warnings.Add(new ScheduleWarning { Task = a.Task, IsCertain = true, Message = "Phụ thuộc vòng hoặc chờ công việc chưa xếp lịch được — kiểm tra lại công việc phụ thuộc." });

        foreach (var a in order)
        {
            var t = a.Task;
            double need = a.RequiredHours;
            if (need <= 0) continue;
            bool assumed = a.RequiredHoursIsAssumed;
            DateTime? lastEnd = null;
            // Không bắt đầu trước khi các công việc phụ thuộc kết thúc (theo lịch đề xuất)
            DateTime? notBefore = t.PlannedStart;
            foreach (var dep in t.DependsOnIds)
                if (proposal.FinishTimes.TryGetValue(dep, out var fin) && (notBefore == null || fin > notBefore)) notBefore = fin;

            for (int i = 0; i < free.Count && need > 1e-6; i++)
            {
                var slot = free[i];
                if (slot.Hours < MinChunkHours - 1e-9) continue;
                if (notBefore.HasValue && slot.End <= notBefore.Value) continue;
                var s = notBefore.HasValue && slot.Start < notBefore.Value ? notBefore.Value : slot.Start;
                var take = Math.Min(need, (slot.End - s).TotalHours);
                if (take < MinChunkHours - 1e-9 && take < need - 1e-9) continue;
                var e = s.AddHours(take);
                proposal.Blocks.Add(new ScheduledBlock { Task = t, Start = s, End = e, IsEstimate = assumed });
                need -= take;
                lastEnd = e;

                // Cập nhật khoảng trống
                var replacement = new List<TimeSlot>();
                if (s > slot.Start) replacement.Add(new TimeSlot(slot.Start, s));
                if (e < slot.End) replacement.Add(new TimeSlot(e, slot.End));
                free.RemoveAt(i);
                free.InsertRange(i, replacement);
                i += replacement.Count - 1;
            }

            if (lastEnd.HasValue) proposal.FinishTimes[t.Id] = lastEnd.Value;

            if (need > 1e-6)
            {
                proposal.Warnings.Add(new ScheduleWarning
                {
                    Task = t,
                    IsCertain = !assumed,
                    Message = $"Không xếp đủ thời gian trong {horizonDays} ngày tới (thiếu {need:0.#} giờ" + (assumed ? ", theo ước tính mặc định)." : ").")
                });
            }
            else if (lastEnd.HasValue && lastEnd.Value > t.DueAt)
            {
                proposal.Warnings.Add(new ScheduleWarning
                {
                    Task = t,
                    IsCertain = !assumed,
                    Message = assumed
                        ? $"Có nguy cơ trễ hạn: theo thời lượng ước tính mặc định, dự kiến xong {Labels.DateTime(lastEnd.Value)} (hạn {Labels.DateTime(t.DueAt)}). Hãy nhập thời lượng dự kiến để đánh giá chính xác."
                        : $"Không đủ thời gian: dự kiến xong {Labels.DateTime(lastEnd.Value)}, sau hạn {Labels.DateTime(t.DueAt)}."
                });
            }
        }

        proposal.Blocks.Sort((x, y) => x.Start.CompareTo(y.Start));
        return proposal;
    }

    public static IEnumerable<TimeSlot> SubtractBusy(IEnumerable<TimeSlot> slots, IReadOnlyCollection<TimeSlot> busy)
    {
        foreach (var slot in slots)
        {
            var pieces = new List<TimeSlot> { slot };
            foreach (var b in busy)
            {
                var next = new List<TimeSlot>();
                foreach (var p in pieces)
                {
                    if (!p.Overlaps(b)) { next.Add(p); continue; }
                    if (b.Start > p.Start) next.Add(new TimeSlot(p.Start, b.Start));
                    if (b.End < p.End) next.Add(new TimeSlot(b.End, p.End));
                }
                pieces = next;
            }
            foreach (var p in pieces) yield return p;
        }
    }
}
