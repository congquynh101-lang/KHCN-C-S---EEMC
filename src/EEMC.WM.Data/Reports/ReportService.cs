using EEMC.WM.Core.Domain;
using EEMC.WM.Core.Services;
using EEMC.WM.Core.Text;
using EEMC.WM.Core.Time;
using EEMC.WM.Data.Services;
using Microsoft.EntityFrameworkCore;

namespace EEMC.WM.Data.Reports;

/// <summary>
/// Tổng hợp báo cáo công việc ngày/tuần/tháng CHỈ từ dữ liệu đã ghi nhận.
/// Không tự tạo kết quả, không suy diễn công việc đã hoàn thành. Phần "khó khăn, kiến nghị"
/// do người dùng tự nhập (nếu có).
/// </summary>
public sealed class ReportService
{
    private readonly IDbContextFactory<AppDbContext> _factory;
    private readonly IClock _clock;
    private readonly SettingsService _settings;

    public ReportService(IDbContextFactory<AppDbContext> factory, IClock clock, SettingsService settings)
    {
        _factory = factory;
        _clock = clock;
        _settings = settings;
    }

    private sealed class Data
    {
        public required List<WorkTask> Tasks { get; init; }
        public required Dictionary<int, Category> Categories { get; init; }
        public required Dictionary<int, Project> Projects { get; init; }
        public required List<Milestone> Milestones { get; init; }

        public string Group(WorkTask t)
        {
            if (t.CategoryId == null || !Categories.TryGetValue(t.CategoryId.Value, out var c)) return "Chưa phân nhóm";
            return c.ParentId.HasValue && Categories.TryGetValue(c.ParentId.Value, out var p) ? p.Name : c.Name;
        }

        public string ProjectOf(WorkTask t) =>
            t.ProjectId.HasValue && Projects.TryGetValue(t.ProjectId.Value, out var p) ? p.Name : t.ProjectName ?? "";
    }

    private async Task<Data> LoadAsync()
    {
        await using var db = await _factory.CreateDbContextAsync();
        var tasks = await db.Tasks.AsNoTracking().Where(t => !t.IsDeleted).ToListAsync();
        await TaskService.FillDependenciesAsync(db, tasks.Where(t => !StatusGroups.IsClosed(t.Status)).ToList());
        return new Data
        {
            Tasks = tasks,
            Categories = await db.Categories.AsNoTracking().ToDictionaryAsync(c => c.Id),
            Projects = await db.Projects.AsNoTracking().ToDictionaryAsync(p => p.Id),
            Milestones = await db.Milestones.AsNoTracking().ToListAsync()
        };
    }

    private ReportDocument NewDoc(ReportKind kind, string title, string period, string file)
    {
        var s = _settings.Current;
        var now = _clock.Now;
        return new ReportDocument
        {
            Kind = kind,
            Title = title,
            Period = period,
            Unit = s.ReportUnit,
            Author = s.ReportAuthor,
            PlaceDate = $"{s.ReportPlace}, ngày {now:dd} tháng {now:MM} năm {now:yyyy}",
            GeneratedAt = now,
            FileBaseName = file
        };
    }

    // ------------------------------------------------------------------ Bảng dùng chung

    private static ReportTable TaskTable(IEnumerable<WorkTask> tasks, DateTime now, Data d, bool showCompleted = false, bool showRemaining = true)
    {
        var t = new ReportTable
        {
            Columns = showCompleted
                ? new[] { "TT", "Mã CV", "Nội dung công việc", "Hạn", "Hoàn thành lúc", "Đúng hạn", "Nhóm/Dự án" }
                : new[] { "TT", "Mã CV", "Nội dung công việc", "Hạn", showRemaining ? "Thời gian còn lại" : "Ưu tiên", "Trạng thái", "Nhóm/Dự án" },
            Widths = new[] { 0.4, 1.1, 4.2, 1.3, 1.4, 1.2, 1.8 }
        };
        int i = 1;
        foreach (var x in tasks)
        {
            var group = string.IsNullOrEmpty(d.ProjectOf(x)) ? d.Group(x) : d.ProjectOf(x);
            t.Rows.Add(showCompleted
                ? new[] { (i++).ToString(), x.Code, x.Title, Labels.DateTime(x.DueAt), Labels.DateTime(x.CompletedAt), x.CompletedAt <= x.DueAt ? "Đúng hạn" : "Trễ hạn", group }
                : new[] { (i++).ToString(), x.Code, x.Title, Labels.DateTime(x.DueAt), showRemaining ? Labels.TimeRemaining(x.DueAt, now) : Labels.Of(x.Priority), Labels.Of(x.Status) + (x.IsBlocked ? $" (chờ {string.Join(", ", x.BlockedBy)})" : ""), group });
        }
        return t;
    }

    private static ReportTable CountTable(params (string Label, string Value)[] rows)
    {
        var t = new ReportTable { Columns = new[] { "TT", "Chỉ tiêu", "Số lượng" }, Widths = new[] { 0.5, 6, 1.5 } };
        int i = 1;
        foreach (var r in rows) t.Rows.Add(new[] { (i++).ToString(), r.Label, r.Value });
        return t;
    }

    private static void AddUserNotes(ReportDocument doc, ReportNotes? notes, string difficultiesHeading, string? proposalsHeading)
    {
        if (!string.IsNullOrWhiteSpace(notes?.Difficulties))
        {
            var s = new ReportSection { Heading = difficultiesHeading, IsUserInput = true };
            s.Paragraphs.AddRange(SplitLines(notes!.Difficulties));
            doc.Sections.Add(s);
        }
        if (proposalsHeading != null && !string.IsNullOrWhiteSpace(notes?.Proposals))
        {
            var s = new ReportSection { Heading = proposalsHeading, IsUserInput = true };
            s.Paragraphs.AddRange(SplitLines(notes!.Proposals));
            doc.Sections.Add(s);
        }
    }

    private static IEnumerable<string> SplitLines(string text) =>
        text.Replace("\r", "").Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => l.Trim()).Where(l => l.Length > 0);

    private static string Pct(int a, int b) => b == 0 ? "—" : $"{a * 100.0 / b:0.#}%";

    /// <summary>Các vướng mắc rút ra TỪ DỮ LIỆU: quá hạn, cần bổ sung hồ sơ, quá ngày dự kiến phản hồi.</summary>
    private static ReportSection IssuesFromData(string heading, IEnumerable<WorkTask> open, DateTime asOf, DateTime now, Data d)
    {
        var s = new ReportSection { Heading = heading };
        var list = open.ToList();
        var overdue = list.Where(t => t.DueAt < asOf).OrderBy(t => t.DueAt).ToList();
        var needDocs = list.Where(t => t.Status == WorkTaskStatus.NeedMoreDocuments).ToList();
        var lateResp = list.Where(t => StatusGroups.IsWaiting(t.Status) && t.ExpectedResponseAt.HasValue && t.ExpectedResponseAt < asOf).ToList();
        var blocked = list.Where(t => t.IsBlocked).ToList();
        if (overdue.Count + needDocs.Count + lateResp.Count + blocked.Count == 0)
        {
            s.Paragraphs.Add("Theo dữ liệu theo dõi, không có công việc quá hạn, cần bổ sung hồ sơ hoặc quá ngày dự kiến phản hồi.");
            return s;
        }
        if (overdue.Count > 0) s.Paragraphs.Add($"- Có {overdue.Count} công việc quá hạn chưa hoàn thành (chi tiết tại bảng dưới).");
        if (needDocs.Count > 0) s.Paragraphs.Add($"- Có {needDocs.Count} công việc đang cần bổ sung hồ sơ.");
        if (lateResp.Count > 0) s.Paragraphs.Add($"- Có {lateResp.Count} công việc đã quá ngày dự kiến nhận phản hồi của đơn vị phối hợp/lãnh đạo, cần đôn đốc.");
        if (blocked.Count > 0) s.Paragraphs.Add($"- Có {blocked.Count} công việc phải chờ công việc khác hoàn thành trước.");
        var rows = overdue.Concat(needDocs).Concat(lateResp).Concat(blocked).DistinctBy(t => t.Id).Take(50);
        s.Table = TaskTable(rows, now, d);
        return s;
    }

    // ------------------------------------------------------------------ Báo cáo ngày

    public async Task<ReportDocument> DailyAsync(DateTime date, ReportNotes? notes = null)
    {
        var d = await LoadAsync();
        var now = _clock.Now;
        var day = date.Date;
        var end = day.AddDays(1);
        var asOf = end < now ? end : now;
        var cal = new WorkCalendar(_settings.Current);
        var next = day.AddDays(1);
        for (int i = 0; i < 14 && !cal.IsWorkDay(next); i++) next = next.AddDays(1);

        var doc = NewDoc(ReportKind.Daily, "BÁO CÁO CÔNG VIỆC NGÀY", $"Ngày {day:dd/MM/yyyy}", $"BaoCaoNgay_{day:yyyyMMdd}");
        var open = d.Tasks.Where(t => !StatusGroups.IsClosed(t.Status) && t.ReceivedAt < end).ToList();

        var done = d.Tasks.Where(t => t.Status == WorkTaskStatus.Completed && t.CompletedAt >= day && t.CompletedAt < end).OrderBy(t => t.CompletedAt).ToList();
        var s1 = new ReportSection { Heading = "I. CÔNG VIỆC ĐÃ HOÀN THÀNH", Table = TaskTable(done, now, d, showCompleted: true), EmptyText = "Không có công việc được ghi nhận hoàn thành trong ngày." };
        doc.Sections.Add(s1);

        var doing = open.Where(t => t.Status is WorkTaskStatus.InProgress or WorkTaskStatus.NeedMoreDocuments).OrderBy(t => t.DueAt).ToList();
        doc.Sections.Add(new ReportSection { Heading = "II. CÔNG VIỆC ĐANG XỬ LÝ", Table = TaskTable(doing, now, d), EmptyText = "Không có công việc ở trạng thái đang thực hiện." });

        var overdue = open.Where(t => t.DueAt < asOf).OrderBy(t => t.DueAt).ToList();
        doc.Sections.Add(new ReportSection { Heading = "III. CÔNG VIỆC QUÁ HẠN", Table = TaskTable(overdue, now, d), EmptyText = "Không có công việc quá hạn." });

        var engine = new PriorityEngine(_settings.Current);
        var nextList = engine.RankDirectWork(open.Where(t => t.DueAt < next.AddDays(1) || (t.PlannedStart.HasValue && t.PlannedStart.Value.Date == next)), now)
            .Select(a => a.Task).ToList();
        var s4 = new ReportSection
        {
            Heading = $"IV. CÔNG VIỆC CẦN XỬ LÝ NGÀY TIẾP THEO ({Labels.DayOfWeekVi(next.DayOfWeek)}, {next:dd/MM/yyyy})",
            Table = TaskTable(nextList, now, d, showRemaining: false),
            EmptyText = "Không có công việc đến hạn hoặc có kế hoạch thực hiện vào ngày tiếp theo."
        };
        s4.Paragraphs.Add("Sắp xếp theo thứ tự ưu tiên đề xuất của hệ thống (thời hạn, mức ưu tiên, thời lượng dự kiến).");
        doc.Sections.Add(s4);
        AddUserNotes(doc, notes, "V. KHÓ KHĂN, VƯỚNG MẮC", "VI. KIẾN NGHỊ, ĐỀ XUẤT");
        return doc;
    }

    // ------------------------------------------------------------------ Báo cáo tuần

    public async Task<ReportDocument> WeeklyAsync(DateTime anyDayInWeek, ReportNotes? notes = null)
    {
        var d = await LoadAsync();
        var now = _clock.Now;
        var ws = TaskService.StartOfWeek(anyDayInWeek);
        var we = ws.AddDays(7);
        var asOf = we < now ? we : now;
        var doc = NewDoc(ReportKind.Weekly, "BÁO CÁO CÔNG VIỆC TUẦN",
            $"Tuần {System.Globalization.ISOWeek.GetWeekOfYear(ws)} (từ ngày {ws:dd/MM/yyyy} đến ngày {we.AddDays(-1):dd/MM/yyyy})", $"BaoCaoTuan_{ws:yyyyMMdd}");

        var received = d.Tasks.Where(t => t.ReceivedAt >= ws && t.ReceivedAt < we).ToList();
        var done = d.Tasks.Where(t => t.Status == WorkTaskStatus.Completed && t.CompletedAt >= ws && t.CompletedAt < we).OrderBy(t => t.CompletedAt).ToList();
        var openAtEnd = d.Tasks.Where(t => t.ReceivedAt < we && !StatusGroups.IsClosed(t.Status)).ToList();
        var waitingCoord = openAtEnd.Where(t => t.Status is WorkTaskStatus.WaitingCoordinator or WorkTaskStatus.WaitingResponse).OrderBy(t => t.ExpectedResponseAt ?? t.DueAt).ToList();
        var waitingApproval = openAtEnd.Where(t => t.Status == WorkTaskStatus.WaitingApproval).OrderBy(t => t.DueAt).ToList();
        int onTime = done.Count(t => t.CompletedAt <= t.DueAt);

        var s0 = new ReportSection { Heading = "I. TỔNG HỢP KẾT QUẢ" };
        s0.Paragraphs.Add($"Trong tuần, tiếp nhận {received.Count} công việc; hoàn thành {done.Count} công việc ({onTime} đúng hạn, {done.Count - onTime} trễ hạn); " +
                          $"còn {openAtEnd.Count} công việc chưa hoàn thành, trong đó {openAtEnd.Count(t => t.DueAt < asOf)} công việc quá hạn.");
        s0.Table = CountTable(
            ("Công việc tiếp nhận trong tuần", received.Count.ToString()),
            ("Công việc hoàn thành trong tuần", done.Count.ToString()),
            ("   – Trong đó hoàn thành đúng hạn", $"{onTime} ({Pct(onTime, done.Count)})"),
            ("Công việc chưa hoàn thành", openAtEnd.Count.ToString()),
            ("   – Trong đó quá hạn", openAtEnd.Count(t => t.DueAt < asOf).ToString()),
            ("Công việc chờ đơn vị phối hợp/phản hồi", waitingCoord.Count.ToString()),
            ("Công việc chờ lãnh đạo phê duyệt", waitingApproval.Count.ToString()));
        doc.Sections.Add(s0);

        doc.Sections.Add(new ReportSection { Heading = "II. CÔNG VIỆC ĐÃ HOÀN THÀNH", Table = TaskTable(done, now, d, showCompleted: true), EmptyText = "Không có công việc được ghi nhận hoàn thành trong tuần." });
        doc.Sections.Add(new ReportSection
        {
            Heading = "III. CÔNG VIỆC CHƯA HOÀN THÀNH",
            Table = TaskTable(openAtEnd.Where(t => !StatusGroups.IsWaiting(t.Status)).OrderBy(t => t.DueAt), now, d),
            EmptyText = "Không có công việc đang xử lý chưa hoàn thành."
        });
        doc.Sections.Add(new ReportSection { Heading = "IV. CÔNG VIỆC CHỜ ĐƠN VỊ PHỐI HỢP, PHẢN HỒI", Table = TaskTable(waitingCoord, now, d), EmptyText = "Không có." });
        doc.Sections.Add(new ReportSection { Heading = "V. CÔNG VIỆC CHỜ LÃNH ĐẠO PHÊ DUYỆT", Table = TaskTable(waitingApproval, now, d), EmptyText = "Không có." });
        doc.Sections.Add(IssuesFromData("VI. KHÓ KHĂN, VƯỚNG MẮC (theo dữ liệu theo dõi)", openAtEnd, asOf, now, d));
        AddUserNotes(doc, notes, "    Ý kiến bổ sung về khó khăn, vướng mắc", null);

        var nws = we;
        var nwe = we.AddDays(7);
        var engine = new PriorityEngine(_settings.Current);
        var plan = engine.RankDirectWork(openAtEnd.Where(t => t.DueAt < nwe || (t.PlannedStart.HasValue && t.PlannedStart >= nws && t.PlannedStart < nwe)), now)
            .Select(a => a.Task).ToList();
        var s7 = new ReportSection
        {
            Heading = $"VII. KẾ HOẠCH TUẦN TIẾP THEO (từ {nws:dd/MM} đến {nwe.AddDays(-1):dd/MM/yyyy})",
            Table = TaskTable(plan, now, d, showRemaining: false),
            EmptyText = "Chưa có công việc đến hạn hoặc có kế hoạch trong tuần tiếp theo."
        };
        s7.Paragraphs.Add("Gồm các công việc chuyển tiếp chưa hoàn thành và công việc đến hạn/có kế hoạch trong tuần tiếp theo, sắp theo thứ tự ưu tiên đề xuất.");
        var ms = d.Milestones.Where(m => m.CompletedAt == null && m.DueDate >= nws && m.DueDate < nwe).ToList();
        foreach (var m in ms)
            s7.Paragraphs.Add($"- Mốc dự án: {m.Name} ({(d.Projects.TryGetValue(m.ProjectId, out var p) ? p.Name : "")}) — hạn {m.DueDate:dd/MM/yyyy}.");
        doc.Sections.Add(s7);
        if (!string.IsNullOrWhiteSpace(notes?.Proposals))
        {
            var sp = new ReportSection { Heading = "VIII. KIẾN NGHỊ, ĐỀ XUẤT", IsUserInput = true };
            sp.Paragraphs.AddRange(SplitLines(notes!.Proposals));
            doc.Sections.Add(sp);
        }
        return doc;
    }

    // ------------------------------------------------------------------ Báo cáo tháng

    public async Task<ReportDocument> MonthlyAsync(int year, int month, ReportNotes? notes = null)
    {
        var d = await LoadAsync();
        var now = _clock.Now;
        var ms = new DateTime(year, month, 1);
        var me = ms.AddMonths(1);
        var asOf = me < now ? me : now;
        var doc = NewDoc(ReportKind.Monthly, "BÁO CÁO CÔNG VIỆC THÁNG", $"Tháng {month:00}/{year}", $"BaoCaoThang_{year}{month:00}");

        var received = d.Tasks.Where(t => t.ReceivedAt >= ms && t.ReceivedAt < me).ToList();
        var done = d.Tasks.Where(t => t.Status == WorkTaskStatus.Completed && t.CompletedAt >= ms && t.CompletedAt < me).ToList();
        var dueInMonth = d.Tasks.Where(t => t.DueAt >= ms && t.DueAt < me && t.Status != WorkTaskStatus.Cancelled).ToList();
        var dueDone = dueInMonth.Count(t => t.Status == WorkTaskStatus.Completed && t.CompletedAt < me);
        var dueDoneOnTime = dueInMonth.Count(t => t.Status == WorkTaskStatus.Completed && t.CompletedAt <= t.DueAt);
        var openAtEnd = d.Tasks.Where(t => t.ReceivedAt < me && !StatusGroups.IsClosed(t.Status)).ToList();
        int onTime = done.Count(t => t.CompletedAt <= t.DueAt);
        var lead = done.Where(t => t.CompletedAt.HasValue).Select(t => (t.CompletedAt!.Value - t.ReceivedAt).TotalDays).ToList();

        var s1 = new ReportSection { Heading = "I. KẾT QUẢ THỰC HIỆN" };
        s1.Paragraphs.Add($"Trong tháng {month:00}/{year}, tiếp nhận {received.Count} công việc, hoàn thành {done.Count} công việc " +
                          $"(trong đó {onTime} công việc hoàn thành đúng hạn).");
        s1.Table = CountTable(
            ("Công việc tiếp nhận trong tháng", received.Count.ToString()),
            ("Công việc hoàn thành trong tháng", done.Count.ToString()),
            ("   – Hoàn thành đúng hạn", $"{onTime} ({Pct(onTime, done.Count)})"),
            ("Thời gian xử lý trung bình (từ khi nhận đến khi hoàn thành)", lead.Count == 0 ? "—" : $"{lead.Average():0.#} ngày"),
            ("Công việc chưa hoàn thành cuối kỳ", openAtEnd.Count.ToString()));
        doc.Sections.Add(s1);

        var s1b = new ReportSection { Heading = "    Kết quả hoàn thành chi tiết", Table = TaskTable(done.OrderBy(t => t.CompletedAt), now, d, showCompleted: true), EmptyText = "Không có công việc được ghi nhận hoàn thành trong tháng." };
        doc.Sections.Add(s1b);

        // Phân tích tiến độ theo nhóm và theo dự án
        var s2 = new ReportSection { Heading = "II. PHÂN TÍCH TIẾN ĐỘ" };
        var byGroup = new ReportTable { Columns = new[] { "TT", "Nhóm nghiệp vụ", "Tiếp nhận", "Hoàn thành", "Đúng hạn", "Chưa hoàn thành", "Quá hạn" }, Widths = new[] { 0.4, 3.6, 1, 1, 1, 1.2, 1 } };
        int i = 1;
        foreach (var g in d.Tasks.Where(t => (t.ReceivedAt >= ms && t.ReceivedAt < me) || (t.CompletedAt >= ms && t.CompletedAt < me) || openAtEnd.Contains(t))
                     .GroupBy(d.Group).OrderBy(g => g.Key))
        {
            byGroup.Rows.Add(new[]
            {
                (i++).ToString(), g.Key,
                g.Count(t => t.ReceivedAt >= ms && t.ReceivedAt < me).ToString(),
                g.Count(t => t.Status == WorkTaskStatus.Completed && t.CompletedAt >= ms && t.CompletedAt < me).ToString(),
                g.Count(t => t.Status == WorkTaskStatus.Completed && t.CompletedAt >= ms && t.CompletedAt < me && t.CompletedAt <= t.DueAt).ToString(),
                g.Count(t => openAtEnd.Contains(t)).ToString(),
                g.Count(t => openAtEnd.Contains(t) && t.DueAt < asOf).ToString()
            });
        }
        s2.Table = byGroup;
        s2.EmptyText = "Không có số liệu trong tháng.";
        doc.Sections.Add(s2);

        var activeProjects = d.Projects.Values.Where(p => !p.IsArchived).ToList();
        if (activeProjects.Count > 0)
        {
            var sp = new ReportSection { Heading = "    Tiến độ các đề tài/dự án KH&CN" };
            var tp = new ReportTable { Columns = new[] { "TT", "Mã", "Đề tài/Dự án", "Tình trạng", "Tiến độ (theo công việc)", "CV quá hạn", "Mốc tiếp theo" }, Widths = new[] { 0.4, 1.1, 3.2, 1.3, 1.3, 1, 2.2 } };
            int j = 1;
            foreach (var p in activeProjects.OrderBy(p => p.Code))
            {
                var pt = d.Tasks.Where(t => t.ProjectId == p.Id).ToList();
                var prog = ProgressCalculator.Weighted(pt.Where(t => t.ParentId == null || pt.All(x => x.Id != t.ParentId)));
                var nextM = d.Milestones.Where(m => m.ProjectId == p.Id && m.CompletedAt == null).OrderBy(m => m.DueDate ?? DateTime.MaxValue).ThenBy(m => m.SortOrder).FirstOrDefault();
                tp.Rows.Add(new[]
                {
                    (j++).ToString(), p.Code, p.Name, Labels.Of(p.Status), prog.HasValue ? $"{prog}%" : "Chưa có công việc",
                    pt.Count(t => !StatusGroups.IsClosed(t.Status) && t.DueAt < asOf).ToString(),
                    nextM == null ? "" : nextM.Name + (nextM.DueDate.HasValue ? $" ({nextM.DueDate:dd/MM/yyyy})" : "")
                });
            }
            sp.Table = tp;
            doc.Sections.Add(sp);
        }

        var backlog = openAtEnd.Where(t => t.DueAt < asOf).OrderBy(t => t.DueAt).ToList();
        doc.Sections.Add(new ReportSection { Heading = "III. CÔNG VIỆC TỒN ĐỌNG (QUÁ HẠN CHƯA HOÀN THÀNH)", Table = TaskTable(backlog, now, d), EmptyText = "Không có công việc tồn đọng quá hạn." });

        var s4 = new ReportSection { Heading = "IV. ĐÁNH GIÁ TỶ LỆ HOÀN THÀNH" };
        s4.Table = CountTable(
            ("Công việc có hạn trong tháng", dueInMonth.Count.ToString()),
            ("   – Đã hoàn thành", $"{dueDone} ({Pct(dueDone, dueInMonth.Count)})"),
            ("   – Hoàn thành đúng hạn", $"{dueDoneOnTime} ({Pct(dueDoneOnTime, dueInMonth.Count)})"),
            ("   – Chưa hoàn thành", (dueInMonth.Count - dueDone).ToString()));
        s4.Paragraphs.Add(dueInMonth.Count == 0
            ? "Không có công việc có hạn hoàn thành trong tháng."
            : $"Tỷ lệ hoàn thành công việc đến hạn trong tháng đạt {Pct(dueDone, dueInMonth.Count)}; tỷ lệ hoàn thành đúng hạn đạt {Pct(dueDoneOnTime, dueInMonth.Count)}.");
        doc.Sections.Add(s4);
        AddUserNotes(doc, notes, "    Ý kiến đánh giá, khó khăn, vướng mắc", null);

        var nms = me;
        var nme = me.AddMonths(1);
        var engine = new PriorityEngine(_settings.Current);
        var focus = engine.RankDirectWork(openAtEnd.Where(t => t.DueAt < nme), now).Select(a => a.Task)
            .Where(t => t.Priority >= TaskPriority.High || t.DueAt < nme).Take(40).ToList();
        var s5 = new ReportSection
        {
            Heading = $"V. NHIỆM VỤ TRỌNG TÂM THÁNG {nms:MM/yyyy}",
            Table = TaskTable(focus, now, d, showRemaining: false),
            EmptyText = "Chưa có công việc đến hạn trong tháng tiếp theo."
        };
        foreach (var m in d.Milestones.Where(m => m.CompletedAt == null && m.DueDate >= nms && m.DueDate < nme).OrderBy(m => m.DueDate))
            s5.Paragraphs.Add($"- Mốc dự án: {m.Name} ({(d.Projects.TryGetValue(m.ProjectId, out var p) ? p.Name : "")}) — hạn {m.DueDate:dd/MM/yyyy}.");
        doc.Sections.Add(s5);
        if (!string.IsNullOrWhiteSpace(notes?.Proposals))
        {
            var sp = new ReportSection { Heading = "VI. KIẾN NGHỊ, ĐỀ XUẤT", IsUserInput = true };
            sp.Paragraphs.AddRange(SplitLines(notes!.Proposals));
            doc.Sections.Add(sp);
        }
        return doc;
    }
}
