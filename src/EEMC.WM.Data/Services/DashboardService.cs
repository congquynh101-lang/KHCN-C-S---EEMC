using EEMC.WM.Core.Domain;
using EEMC.WM.Core.Text;
using EEMC.WM.Core.Time;
using Microsoft.EntityFrameworkCore;

namespace EEMC.WM.Data.Services;

public sealed record ChartPoint(string Label, double Value, string? Key = null);

public sealed class WeeklyFlow
{
    public required string Label { get; init; }
    public DateTime WeekStart { get; init; }
    public int Received { get; init; }
    public int Completed { get; init; }
    public int Backlog { get; init; }
}

public sealed class DashboardStats
{
    public int Open { get; init; }
    public int Today { get; init; }
    public int HighOrUrgent { get; init; }
    public int Overdue { get; init; }
    public int DueSoon { get; init; }
    public int WaitingResponse { get; init; }
    public int WaitingApproval { get; init; }
    public int CompletedThisWeek { get; init; }
    public List<ChartPoint> ByStatus { get; init; } = new();
    public List<ChartPoint> ByCategory { get; init; } = new();
    public List<WeeklyFlow> Weekly { get; init; } = new();
    /// <summary>Tỷ lệ hoàn thành đúng hạn (8 tuần gần nhất); null nếu chưa có công việc hoàn thành.</summary>
    public double? OnTimeRate { get; init; }
    public int CompletedLast8Weeks { get; init; }
    public int CompletedOnTimeLast8Weeks { get; init; }
    /// <summary>Thời gian xử lý trung bình (ngày) từ lúc nhận đến lúc hoàn thành, 8 tuần gần nhất.</summary>
    public double? AvgLeadDays { get; init; }
}

/// <summary>Thống kê cho trang tổng quan. Các phép đếm chạy trực tiếp trên CSDL.</summary>
public sealed class DashboardService
{
    private readonly IDbContextFactory<AppDbContext> _factory;
    private readonly IClock _clock;
    private readonly SettingsService _settings;

    public DashboardService(IDbContextFactory<AppDbContext> factory, IClock clock, SettingsService settings)
    {
        _factory = factory;
        _clock = clock;
        _settings = settings;
    }

    public async Task<DashboardStats> GetStatsAsync(int weeks = 8)
    {
        var now = _clock.Now;
        var today = now.Date;
        var tomorrow = today.AddDays(1);
        var soonEnd = today.AddDays(_settings.Current.DueSoonDays + 1);
        var weekStart = TaskService.StartOfWeek(now);
        var rangeStart = weekStart.AddDays(-7 * (weeks - 1));

        await using var db = await _factory.CreateDbContextAsync();
        var live = db.Tasks.AsNoTracking().Where(t => !t.IsDeleted);
        var open = live.Where(t => t.Status != WorkTaskStatus.Completed && t.Status != WorkTaskStatus.Cancelled);

        var stats = new
        {
            Open = await open.CountAsync(),
            Today = await open.CountAsync(t => (t.DueAt >= today && t.DueAt < tomorrow) || (t.PlannedStart != null && t.PlannedStart >= today && t.PlannedStart < tomorrow)),
            High = await open.CountAsync(t => t.Priority == TaskPriority.High || t.Priority == TaskPriority.Urgent),
            Overdue = await open.CountAsync(t => t.DueAt < now),
            DueSoon = await open.CountAsync(t => t.DueAt >= now && t.DueAt < soonEnd),
            WaitResp = await open.CountAsync(t => t.Status == WorkTaskStatus.WaitingResponse || t.Status == WorkTaskStatus.WaitingCoordinator),
            WaitAppr = await open.CountAsync(t => t.Status == WorkTaskStatus.WaitingApproval),
            DoneWeek = await live.CountAsync(t => t.Status == WorkTaskStatus.Completed && t.CompletedAt >= weekStart),
        };

        var byStatusRaw = await open.GroupBy(t => t.Status).Select(g => new { g.Key, Count = g.Count() }).ToListAsync();
        var byStatus = Enum.GetValues<WorkTaskStatus>()
            .Where(s => !StatusGroups.IsClosed(s))
            .Select(s => new ChartPoint(Labels.Of(s), byStatusRaw.FirstOrDefault(x => x.Key == s)?.Count ?? 0, s.ToString()))
            .ToList();

        // Theo nhóm nghiệp vụ (cấp nhóm)
        var catRaw = await open.GroupBy(t => t.CategoryId).Select(g => new { g.Key, Count = g.Count() }).ToListAsync();
        var cats = await db.Categories.AsNoTracking().ToDictionaryAsync(c => c.Id);
        var byCategory = catRaw
            .Select(x =>
            {
                if (x.Key == null || !cats.TryGetValue(x.Key.Value, out var c)) return (Name: "Chưa phân nhóm", x.Count);
                var group = c.ParentId.HasValue && cats.TryGetValue(c.ParentId.Value, out var p) ? p : c;
                return (group.Name, x.Count);
            })
            .GroupBy(x => x.Item1)
            .Select(g => new ChartPoint(g.Key, g.Sum(x => x.Count)))
            .OrderByDescending(p => p.Value)
            .ToList();

        // Dòng công việc theo tuần (chỉ lấy các cột ngày, nhẹ)
        var rows = await live.Where(t => t.ReceivedAt < weekStart.AddDays(7) && (t.ClosedAt == null || t.ClosedAt >= rangeStart))
            .Select(t => new { t.ReceivedAt, t.CompletedAt, t.ClosedAt, t.DueAt, t.Status })
            .ToListAsync();
        var weekly = new List<WeeklyFlow>();
        for (int i = 0; i < weeks; i++)
        {
            var ws = rangeStart.AddDays(7 * i);
            var we = ws.AddDays(7);
            var end = we > now ? now : we;
            weekly.Add(new WeeklyFlow
            {
                WeekStart = ws,
                Label = ws.ToString("dd/MM"),
                Received = rows.Count(r => r.ReceivedAt >= ws && r.ReceivedAt < we),
                Completed = rows.Count(r => r.Status == WorkTaskStatus.Completed && r.CompletedAt >= ws && r.CompletedAt < we),
                Backlog = rows.Count(r => r.ReceivedAt < end && (r.ClosedAt == null || r.ClosedAt >= end))
            });
        }

        var done = await live.Where(t => t.Status == WorkTaskStatus.Completed && t.CompletedAt >= rangeStart)
            .Select(t => new { t.CompletedAt, t.DueAt, t.ReceivedAt }).ToListAsync();
        int onTime = done.Count(d => d.CompletedAt <= d.DueAt);

        return new DashboardStats
        {
            Open = stats.Open,
            Today = stats.Today,
            HighOrUrgent = stats.High,
            Overdue = stats.Overdue,
            DueSoon = stats.DueSoon,
            WaitingResponse = stats.WaitResp,
            WaitingApproval = stats.WaitAppr,
            CompletedThisWeek = stats.DoneWeek,
            ByStatus = byStatus,
            ByCategory = byCategory,
            Weekly = weekly,
            CompletedLast8Weeks = done.Count,
            CompletedOnTimeLast8Weeks = onTime,
            OnTimeRate = done.Count == 0 ? null : onTime / (double)done.Count,
            AvgLeadDays = done.Count == 0 ? null : done.Average(d => (d.CompletedAt!.Value - d.ReceivedAt).TotalDays)
        };
    }
}
