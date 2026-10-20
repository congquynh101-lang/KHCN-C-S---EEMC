using EEMC.WM.Core.Domain;
using EEMC.WM.Core.Services;
using EEMC.WM.Core.Text;
using EEMC.WM.Core.Time;
using Microsoft.EntityFrameworkCore;

namespace EEMC.WM.Data.Services;

/// <summary>Nghiệp vụ thêm/sửa/xóa/truy vấn công việc. Mỗi thao tác ghi ngay xuống CSDL.</summary>
public sealed class TaskService
{
    private readonly IDbContextFactory<AppDbContext> _factory;
    private readonly IClock _clock;
    private readonly SettingsService _settings;

    public TaskService(IDbContextFactory<AppDbContext> factory, IClock clock, SettingsService settings)
    {
        _factory = factory;
        _clock = clock;
        _settings = settings;
    }

    /// <summary>Phát sinh khi dữ liệu công việc thay đổi (để các màn hình làm mới).</summary>
    public event EventHandler? TasksChanged;

    private void RaiseChanged() => TasksChanged?.Invoke(this, EventArgs.Empty);

    public static DateTime StartOfWeek(DateTime d) => d.Date.AddDays(-(((int)d.DayOfWeek + 6) % 7));

    // ------------------------------------------------------------------ Đọc

    public async Task<WorkTask?> GetAsync(int id)
    {
        await using var db = await _factory.CreateDbContextAsync();
        return await db.Tasks.AsNoTracking().Include(t => t.Category).ThenInclude(c => c!.Parent)
            .FirstOrDefaultAsync(t => t.Id == id);
    }

    /// <summary>Tất cả công việc chưa đóng, chưa xóa (kèm thông tin phụ thuộc).</summary>
    public async Task<List<WorkTask>> GetOpenTasksAsync()
    {
        await using var db = await _factory.CreateDbContextAsync();
        var list = await db.Tasks.AsNoTracking()
            .Where(t => !t.IsDeleted && t.Status != WorkTaskStatus.Completed && t.Status != WorkTaskStatus.Cancelled)
            .ToListAsync();
        await FillDependenciesAsync(db, list);
        return list;
    }

    /// <summary>Điền DependsOnIds và BlockedBy (mã các công việc phụ thuộc chưa hoàn thành).</summary>
    internal static async Task FillDependenciesAsync(AppDbContext db, IReadOnlyCollection<WorkTask> tasks)
    {
        if (tasks.Count == 0) return;
        var ids = tasks.Select(t => t.Id).ToList();
        var deps = await db.TaskDependencies.AsNoTracking().Where(d => ids.Contains(d.TaskId)).ToListAsync();
        if (deps.Count == 0) return;
        var preIds = deps.Select(d => d.DependsOnTaskId).Distinct().ToList();
        var pre = await db.Tasks.AsNoTracking().Where(t => preIds.Contains(t.Id))
            .Select(t => new { t.Id, t.Code, t.Status, t.IsDeleted }).ToDictionaryAsync(t => t.Id);
        foreach (var t in tasks)
        {
            t.DependsOnIds = deps.Where(d => d.TaskId == t.Id).Select(d => d.DependsOnTaskId).ToList();
            t.BlockedBy = t.DependsOnIds
                .Where(id => pre.TryGetValue(id, out var p) && !p.IsDeleted && p.Status != WorkTaskStatus.Completed && p.Status != WorkTaskStatus.Cancelled)
                .Select(id => pre[id].Code).ToList();
        }
    }

    // ------------------------------------------------------------------ Công việc con

    public async Task<List<WorkTask>> GetChildrenAsync(int parentId)
    {
        await using var db = await _factory.CreateDbContextAsync();
        var list = await db.Tasks.AsNoTracking().Where(t => t.ParentId == parentId && !t.IsDeleted)
            .OrderBy(t => t.PlannedStart ?? t.DueAt).ThenBy(t => t.Id).ToListAsync();
        await FillDependenciesAsync(db, list);
        return list;
    }

    /// <summary>Tạo công việc con, kế thừa dự án/nhóm/người giao của công việc cha.</summary>
    public async Task<WorkTask> CreateSubtaskAsync(int parentId, WorkTask child)
    {
        var parent = await GetAsync(parentId) ?? throw new InvalidOperationException("Không tìm thấy công việc cha.");
        if (parent.ParentId != null) throw new ValidationException(new[] { "Chỉ hỗ trợ một cấp công việc con." });
        child.ParentId = parentId;
        child.ProjectId ??= parent.ProjectId;
        child.ProjectName ??= parent.ProjectName;
        child.CategoryId ??= parent.CategoryId;
        child.AssignedBy ??= parent.AssignedBy;
        if (child.ReceivedAt == default) child.ReceivedAt = _clock.Now;
        var created = await CreateAsync(child);
        await RecalcParentAsync(parentId);
        return created;
    }

    /// <summary>Cập nhật tiến độ công việc cha theo các công việc con (không tự đổi trạng thái).</summary>
    public async Task RecalcParentAsync(int? parentId)
    {
        if (parentId == null) return;
        await using var db = await _factory.CreateDbContextAsync();
        var parent = await db.Tasks.FirstOrDefaultAsync(t => t.Id == parentId);
        if (parent == null) return;
        var children = await db.Tasks.AsNoTracking().Where(t => t.ParentId == parentId).ToListAsync();
        var p = ProgressCalculator.Weighted(children);
        if (p.HasValue && parent.Progress != p.Value)
        {
            parent.Progress = p.Value;
            parent.UpdatedAt = _clock.Now;
            await db.SaveChangesAsync();
        }
    }

    // ------------------------------------------------------------------ Phụ thuộc

    public sealed record DependencyInfo(WorkTask Task, bool IsPredecessor);

    /// <summary>Công việc phải xong trước (IsPredecessor) và công việc đang chờ công việc này.</summary>
    public async Task<List<DependencyInfo>> GetDependenciesAsync(int taskId)
    {
        await using var db = await _factory.CreateDbContextAsync();
        var pre = await db.TaskDependencies.Where(d => d.TaskId == taskId).Select(d => d.DependsOnTaskId).ToListAsync();
        var suc = await db.TaskDependencies.Where(d => d.DependsOnTaskId == taskId).Select(d => d.TaskId).ToListAsync();
        var all = pre.Concat(suc).ToList();
        var tasks = await db.Tasks.AsNoTracking().Where(t => all.Contains(t.Id)).ToDictionaryAsync(t => t.Id);
        return pre.Where(tasks.ContainsKey).Select(id => new DependencyInfo(tasks[id], true))
            .Concat(suc.Where(tasks.ContainsKey).Select(id => new DependencyInfo(tasks[id], false))).ToList();
    }

    public async Task AddDependencyAsync(int taskId, int dependsOnTaskId)
    {
        await using var db = await _factory.CreateDbContextAsync();
        if (!await db.Tasks.AnyAsync(t => t.Id == taskId) || !await db.Tasks.AnyAsync(t => t.Id == dependsOnTaskId))
            throw new InvalidOperationException("Không tìm thấy công việc.");
        var edges = await db.TaskDependencies.Select(d => new { d.TaskId, d.DependsOnTaskId }).ToListAsync();
        if (edges.Any(e => e.TaskId == taskId && e.DependsOnTaskId == dependsOnTaskId)) return;
        if (ProgressCalculator.CreatesCycle(taskId, dependsOnTaskId, edges.Select(e => (e.TaskId, e.DependsOnTaskId)).ToList()))
            throw new ValidationException(new[] { "Không thể thêm: sẽ tạo vòng phụ thuộc (A chờ B, B lại chờ A)." });
        db.TaskDependencies.Add(new TaskDependency { TaskId = taskId, DependsOnTaskId = dependsOnTaskId });
        var pre = await db.Tasks.AsNoTracking().Where(t => t.Id == dependsOnTaskId).Select(t => t.Code).FirstAsync();
        db.TaskHistories.Add(new TaskHistory { TaskId = taskId, At = _clock.Now, Action = "Phụ thuộc", Detail = $"Phải chờ {pre} hoàn thành trước" });
        await db.SaveChangesAsync();
        RaiseChanged();
    }

    public async Task RemoveDependencyAsync(int taskId, int dependsOnTaskId)
    {
        await using var db = await _factory.CreateDbContextAsync();
        await db.TaskDependencies.Where(d => d.TaskId == taskId && d.DependsOnTaskId == dependsOnTaskId).ExecuteDeleteAsync();
        RaiseChanged();
    }

    /// <summary>Tìm nhanh công việc theo mã/nội dung (để chọn công việc phụ thuộc, công việc cha...).</summary>
    public async Task<List<WorkTask>> SearchQuickAsync(string text, int take = 15)
    {
        await using var db = await _factory.CreateDbContextAsync();
        IQueryable<WorkTask> q = db.Tasks.AsNoTracking().Where(t => !t.IsDeleted);
        foreach (var term in TextNormalizer.Normalize(text).Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var pattern = "%" + term.Replace("%", "").Replace("_", "") + "%";
            q = q.Where(t => EF.Functions.Like(t.SearchText, pattern));
        }
        return await q.OrderByDescending(t => t.UpdatedAt).Take(take).ToListAsync();
    }

    /// <summary>Công việc có hạn, thời điểm bắt đầu kế hoạch hoặc thời điểm hoàn thành rơi vào khoảng [from, to).</summary>
    public async Task<List<WorkTask>> GetTasksInRangeAsync(DateTime from, DateTime to, bool includeClosed = true)
    {
        await using var db = await _factory.CreateDbContextAsync();
        var q = db.Tasks.AsNoTracking().Where(t => !t.IsDeleted &&
            ((t.DueAt >= from && t.DueAt < to) ||
             (t.PlannedStart != null && t.PlannedStart >= from && t.PlannedStart < to)));
        if (!includeClosed)
            q = q.Where(t => t.Status != WorkTaskStatus.Completed && t.Status != WorkTaskStatus.Cancelled);
        return await q.OrderBy(t => t.DueAt).ToListAsync();
    }

    public async Task<List<TaskHistory>> GetHistoryAsync(int taskId)
    {
        await using var db = await _factory.CreateDbContextAsync();
        return await db.TaskHistories.AsNoTracking().Where(h => h.TaskId == taskId)
            .OrderByDescending(h => h.At).ThenByDescending(h => h.Id).ToListAsync();
    }

    public async Task<PagedResult<WorkTask>> QueryAsync(TaskQuery query)
    {
        await using var db = await _factory.CreateDbContextAsync();
        var now = _clock.Now;
        var q = BuildFilter(db, query, now);

        var total = await q.CountAsync();
        int page = Math.Max(1, query.Page);
        int size = Math.Clamp(query.PageSize, 10, 1000);

        List<WorkTask> items;
        if (query.Sort == SortField.Suggested)
        {
            // Thứ tự đề xuất: tính điểm trong bộ nhớ trên tập đã lọc
            var all = await q.Include(t => t.Category).ThenInclude(c => c!.Parent).Include(t => t.Project).ToListAsync();
            await FillDependenciesAsync(db, all);
            var engine = new PriorityEngine(_settings.Current);
            items = all.Select(t => (t, a: engine.Assess(t, now)))
                .OrderBy(x => StatusGroups.IsClosed(x.t.Status) ? 2 : StatusGroups.IsDirectWork(x.t.Status) ? 0 : 1)
                .ThenByDescending(x => x.a.Score)
                .ThenBy(x => x.t.DueAt)
                .Skip((page - 1) * size).Take(size)
                .Select(x => x.t).ToList();
        }
        else
        {
            var ordered = ApplySort(q, query.Sort, query.SortDescending, null);
            if (query.ThenSort.HasValue && query.ThenSort != query.Sort)
                ordered = ApplySort(q, query.ThenSort.Value, query.ThenSortDescending, ordered);
            ordered = ordered.ThenBy(t => t.Id);
            items = await ordered.Skip((page - 1) * size).Take(size)
                .Include(t => t.Category).ThenInclude(c => c!.Parent).Include(t => t.Project).ToListAsync();
            await FillDependenciesAsync(db, items);
        }

        return new PagedResult<WorkTask> { Items = items, Total = total, Page = page, PageSize = size };
    }

    /// <summary>Tất cả kết quả lọc (không phân trang) để xuất Excel.</summary>
    public async Task<List<WorkTask>> QueryAllAsync(TaskQuery query)
    {
        await using var db = await _factory.CreateDbContextAsync();
        var q = BuildFilter(db, query, _clock.Now);
        return await ApplySort(q, query.Sort == SortField.Suggested ? SortField.DueAt : query.Sort, query.SortDescending, null)
            .ThenBy(t => t.Id)
            .Include(t => t.Category).ThenInclude(c => c!.Parent).ToListAsync();
    }

    private IQueryable<WorkTask> BuildFilter(AppDbContext db, TaskQuery query, DateTime now)
    {
        IQueryable<WorkTask> q = db.Tasks.AsNoTracking().Where(t => t.IsDeleted == query.Deleted);

        if (!query.Deleted)
        {
            q = query.StatusFilter switch
            {
                StatusFilter.Open => q.Where(t => t.Status != WorkTaskStatus.Completed && t.Status != WorkTaskStatus.Cancelled),
                StatusFilter.DirectWork => q.Where(t => t.Status == WorkTaskStatus.NotStarted || t.Status == WorkTaskStatus.InProgress || t.Status == WorkTaskStatus.NeedMoreDocuments),
                StatusFilter.Waiting => q.Where(t => t.Status == WorkTaskStatus.WaitingCoordinator || t.Status == WorkTaskStatus.WaitingResponse || t.Status == WorkTaskStatus.WaitingApproval),
                StatusFilter.Specific when query.Status.HasValue => q.Where(t => t.Status == query.Status.Value),
                _ => q
            };
        }

        if (query.Priority.HasValue) q = q.Where(t => t.Priority == query.Priority.Value);
        if (query.MinPriority.HasValue) q = q.Where(t => t.Priority >= query.MinPriority.Value);
        if (query.OnlyWithDocuments)
            q = q.Where(t => t.FolderPath != null || t.DocumentNumber != null || t.DocumentStatus != DocumentStatus.None);
        if (query.DocumentStatus.HasValue) q = q.Where(t => t.DocumentStatus == query.DocumentStatus.Value);

        if (query.CategoryGroupId.HasValue)
        {
            var gid = query.CategoryGroupId.Value;
            q = q.Where(t => t.CategoryId == gid || (t.Category != null && t.Category.ParentId == gid));
        }

        var today = now.Date;
        var tomorrow = today.AddDays(1);
        var week = StartOfWeek(now);
        var month = new DateTime(now.Year, now.Month, 1);
        switch (query.Time)
        {
            case TimeFilter.Overdue:
                q = q.Where(t => t.DueAt < now && t.Status != WorkTaskStatus.Completed && t.Status != WorkTaskStatus.Cancelled);
                break;
            case TimeFilter.Today:
                q = q.Where(t => (t.DueAt >= today && t.DueAt < tomorrow) || (t.PlannedStart != null && t.PlannedStart >= today && t.PlannedStart < tomorrow));
                break;
            case TimeFilter.DueSoon:
                var soonEnd = today.AddDays(_settings.Current.DueSoonDays + 1);
                q = q.Where(t => t.DueAt >= now && t.DueAt < soonEnd);
                break;
            case TimeFilter.ThisWeek:
                q = q.Where(t => t.DueAt >= week && t.DueAt < week.AddDays(7));
                break;
            case TimeFilter.NextWeek:
                q = q.Where(t => t.DueAt >= week.AddDays(7) && t.DueAt < week.AddDays(14));
                break;
            case TimeFilter.ThisMonth:
                q = q.Where(t => t.DueAt >= month && t.DueAt < month.AddMonths(1));
                break;
            case TimeFilter.ReceivedThisWeek:
                q = q.Where(t => t.ReceivedAt >= week && t.ReceivedAt < week.AddDays(7));
                break;
            case TimeFilter.CompletedThisWeek:
                q = q.Where(t => t.CompletedAt != null && t.CompletedAt >= week && t.CompletedAt < week.AddDays(7));
                break;
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            foreach (var term in TextNormalizer.Normalize(query.Search).Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                var pattern = "%" + term.Replace("%", "").Replace("_", "") + "%";
                q = q.Where(t => EF.Functions.Like(t.SearchText, pattern));
            }
        }
        return q;
    }

    private static IOrderedQueryable<WorkTask> ApplySort(IQueryable<WorkTask> q, SortField f, bool desc, IOrderedQueryable<WorkTask>? prev)
    {
        IOrderedQueryable<WorkTask> By<TKey>(System.Linq.Expressions.Expression<Func<WorkTask, TKey>> key) =>
            prev == null
                ? (desc ? q.OrderByDescending(key) : q.OrderBy(key))
                : (desc ? prev.ThenByDescending(key) : prev.ThenBy(key));

        return f switch
        {
            SortField.Priority => By(t => t.Priority),
            SortField.ReceivedAt => By(t => t.ReceivedAt),
            SortField.Status => By(t => t.Status),
            SortField.Code => By(t => t.Code),
            SortField.Title => By(t => t.Title),
            SortField.UpdatedAt => By(t => t.UpdatedAt),
            _ => By(t => t.DueAt),
        };
    }

    // ------------------------------------------------------------------ Ghi

    public async Task<WorkTask> CreateAsync(WorkTask input)
    {
        var errors = TaskValidator.Validate(input);
        if (errors.Count > 0) throw new ValidationException(errors);

        await using var db = await _factory.CreateDbContextAsync();
        await using var tx = await db.Database.BeginTransactionAsync();
        var now = _clock.Now;

        var t = input;
        t.Id = 0;
        t.Category = null;
        t.Title = t.Title.Trim();
        t.Code = await NextCodeAsync(db, t.ReceivedAt.Year);
        t.CreatedAt = now;
        t.UpdatedAt = now;
        ApplyStatusTimestamps(t, null, now);
        t.RefreshSearchText();
        db.Tasks.Add(t);
        await db.SaveChangesAsync();

        db.TaskHistories.Add(new TaskHistory
        {
            TaskId = t.Id, At = now, Action = "Tạo mới",
            Detail = $"Hạn {Labels.DateTime(t.DueAt)}, ưu tiên {Labels.Of(t.Priority)}"
        });
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        RaiseChanged();
        return t;
    }

    internal static async Task<string> NextCodeAsync(AppDbContext db, int year)
    {
        var counter = await db.CodeCounters.FirstOrDefaultAsync(c => c.Year == year);
        if (counter == null)
        {
            counter = new CodeCounter { Year = year, LastNumber = 0 };
            db.CodeCounters.Add(counter);
        }
        // Luôn vượt qua số lớn nhất đang có (kể cả mã nhận về từ máy khác qua đồng bộ hoặc phục hồi)
        var prefix = $"CV-{year}-";
        var codes = await db.Tasks.Where(t => t.Code.StartsWith(prefix)).Select(t => t.Code).ToListAsync();
        int max = codes.Select(c => int.TryParse(c.AsSpan(prefix.Length), out var n) ? n : 0).DefaultIfEmpty(0).Max();
        counter.LastNumber = Math.Max(counter.LastNumber, max) + 1;
        await db.SaveChangesAsync();
        return $"CV-{year}-{counter.LastNumber:00000}";
    }

    /// <summary>Báo cho giao diện làm mới sau khi nhận dữ liệu đồng bộ.</summary>
    public void NotifyExternalChange() => RaiseChanged();

    public async Task<WorkTask> UpdateAsync(WorkTask input)
    {
        var errors = TaskValidator.Validate(input);
        if (errors.Count > 0) throw new ValidationException(errors);

        await using var db = await _factory.CreateDbContextAsync();
        var t = await db.Tasks.FirstOrDefaultAsync(x => x.Id == input.Id)
                ?? throw new InvalidOperationException("Không tìm thấy công việc (có thể đã bị xóa).");
        var now = _clock.Now;
        var categoryNames = await db.Categories.AsNoTracking().ToDictionaryAsync(c => c.Id, c => c.Name);
        var changes = Diff(t, input, categoryNames);
        if (changes.Count == 0) return t;

        var oldStatus = t.Status;
        var oldDue = t.DueAt;

        t.ReceivedAt = input.ReceivedAt;
        t.DueAt = input.DueAt;
        t.Title = input.Title.Trim();
        t.Priority = input.Priority;
        t.Status = input.Status;
        t.CategoryId = input.CategoryId;
        t.ProjectName = input.ProjectName;
        t.AssignedBy = input.AssignedBy;
        t.Assignee = input.Assignee;
        t.Coordinator = input.Coordinator;
        t.EstimatedHours = input.EstimatedHours;
        t.PlannedStart = input.PlannedStart;
        t.IsFixedTime = input.IsFixedTime;
        t.Progress = input.Progress;
        t.Deliverable = input.Deliverable;
        t.DocumentStatus = input.DocumentStatus;
        t.DocumentNumber = input.DocumentNumber;
        t.LeaderOpinionNeeded = input.LeaderOpinionNeeded;
        t.Notes = input.Notes;
        t.FolderPath = input.FolderPath;
        t.ExpectedResponseAt = input.ExpectedResponseAt;
        var oldParent = t.ParentId;
        t.ParentId = input.ParentId;
        t.ProjectId = input.ProjectId;
        t.Weight = input.Weight > 0 ? input.Weight : 1;
        t.UpdatedAt = now;
        ApplyStatusTimestamps(t, oldStatus, now);
        t.RefreshSearchText();
        if (t.ParentId == t.Id) throw new ValidationException(new[] { "Công việc không thể là công việc con của chính nó." });

        if (oldDue != t.DueAt)
        {
            // Hạn mới: đặt lại các mốc nhắc
            await db.ReminderLogs.Where(r => r.TaskId == t.Id).ExecuteDeleteAsync();
            t.SnoozeUntil = null;
        }

        db.TaskHistories.Add(new TaskHistory
        {
            TaskId = t.Id, At = now,
            Action = oldStatus != t.Status ? "Cập nhật, đổi trạng thái" : "Cập nhật",
            Detail = string.Join("; ", changes)
        });
        await db.SaveChangesAsync();
        await RecalcParentAsync(oldParent);
        if (t.ParentId != oldParent) await RecalcParentAsync(t.ParentId);
        RaiseChanged();
        return t;
    }

    private static void ApplyStatusTimestamps(WorkTask t, WorkTaskStatus? oldStatus, DateTime now)
    {
        if (t.Status == WorkTaskStatus.InProgress && t.StartedAt == null) t.StartedAt = now;
        if (t.Status == WorkTaskStatus.Completed)
        {
            if (oldStatus != WorkTaskStatus.Completed || t.CompletedAt == null)
            {
                t.CompletedAt = now;
                t.ClosedAt = now;
            }
        }
        else if (t.Status == WorkTaskStatus.Cancelled)
        {
            t.CompletedAt = null;
            if (oldStatus != WorkTaskStatus.Cancelled || t.ClosedAt == null) t.ClosedAt = now;
        }
        else
        {
            t.CompletedAt = null;
            t.ClosedAt = null;
        }
    }

    /// <summary>Đổi trạng thái. Trạng thái Hoàn thành chỉ được gọi sau khi người dùng xác nhận trên giao diện.</summary>
    public async Task ChangeStatusAsync(int id, WorkTaskStatus status, string? note = null)
    {
        await using var db = await _factory.CreateDbContextAsync();
        var t = await db.Tasks.FirstOrDefaultAsync(x => x.Id == id)
                ?? throw new InvalidOperationException("Không tìm thấy công việc.");
        if (t.Status == status && string.IsNullOrWhiteSpace(note)) return;
        var now = _clock.Now;
        var old = t.Status;
        t.Status = status;
        if (status == WorkTaskStatus.Completed && t.Progress < 100) t.Progress = 100;
        t.UpdatedAt = now;
        ApplyStatusTimestamps(t, old, now);
        db.TaskHistories.Add(new TaskHistory
        {
            TaskId = id, At = now, Action = "Đổi trạng thái",
            Detail = $"{Labels.Of(old)} → {Labels.Of(status)}" + (string.IsNullOrWhiteSpace(note) ? "" : $". {note.Trim()}")
        });
        await db.SaveChangesAsync();
        await RecalcParentAsync(t.ParentId);
        RaiseChanged();
    }

    public async Task AddCommentAsync(int taskId, string text)
    {
        if (string.IsNullOrWhiteSpace(text)) throw new ValidationException(new[] { "Nội dung trao đổi không được để trống." });
        await using var db = await _factory.CreateDbContextAsync();
        if (!await db.Tasks.AnyAsync(t => t.Id == taskId)) throw new InvalidOperationException("Không tìm thấy công việc.");
        db.TaskHistories.Add(new TaskHistory { TaskId = taskId, At = _clock.Now, Action = "Trao đổi, xử lý", Detail = text.Trim(), IsComment = true });
        await db.SaveChangesAsync();
    }

    public async Task<WorkTask> DuplicateAsync(int id)
    {
        var src = await GetAsync(id) ?? throw new InvalidOperationException("Không tìm thấy công việc.");
        var copy = src.CloneForCopy();
        copy.Title = src.Title;
        copy.ReceivedAt = _clock.Now;
        if (copy.DueAt < copy.ReceivedAt) copy.DueAt = copy.ReceivedAt.Date.AddDays(1).AddHours(17);
        var created = await CreateAsync(copy);
        await using var db = await _factory.CreateDbContextAsync();
        db.TaskHistories.Add(new TaskHistory { TaskId = created.Id, At = _clock.Now, Action = "Sao chép", Detail = $"Sao chép từ {src.Code}" });
        await db.SaveChangesAsync();
        return created;
    }

    public async Task SnoozeAsync(int id, DateTime until)
    {
        await using var db = await _factory.CreateDbContextAsync();
        var t = await db.Tasks.FirstOrDefaultAsync(x => x.Id == id);
        if (t == null) return;
        t.SnoozeUntil = until;
        await db.SaveChangesAsync();
    }

    public async Task SetPlannedStartAsync(int id, DateTime? plannedStart)
    {
        await using var db = await _factory.CreateDbContextAsync();
        var t = await db.Tasks.FirstOrDefaultAsync(x => x.Id == id) ?? throw new InvalidOperationException("Không tìm thấy công việc.");
        var now = _clock.Now;
        t.PlannedStart = plannedStart;
        t.UpdatedAt = now;
        db.TaskHistories.Add(new TaskHistory { TaskId = id, At = now, Action = "Lập lịch", Detail = plannedStart.HasValue ? $"Bắt đầu theo kế hoạch: {Labels.DateTime(plannedStart)}" : "Bỏ thời gian bắt đầu theo kế hoạch" });
        await db.SaveChangesAsync();
        RaiseChanged();
    }

    /// <summary>Chuyển vào thùng rác (có thể phục hồi).</summary>
    public async Task MoveToTrashAsync(IReadOnlyCollection<int> ids)
    {
        if (ids.Count == 0) return;
        await using var db = await _factory.CreateDbContextAsync();
        var now = _clock.Now;
        var list = await db.Tasks.Where(t => ids.Contains(t.Id)).ToListAsync();
        foreach (var t in list)
        {
            t.IsDeleted = true;
            t.DeletedAt = now;
            db.TaskHistories.Add(new TaskHistory { TaskId = t.Id, At = now, Action = "Xóa", Detail = "Chuyển vào thùng rác" });
        }
        await db.SaveChangesAsync();
        RaiseChanged();
    }

    public async Task RestoreAsync(IReadOnlyCollection<int> ids)
    {
        if (ids.Count == 0) return;
        await using var db = await _factory.CreateDbContextAsync();
        var now = _clock.Now;
        var list = await db.Tasks.Where(t => ids.Contains(t.Id)).ToListAsync();
        foreach (var t in list)
        {
            t.IsDeleted = false;
            t.DeletedAt = null;
            db.TaskHistories.Add(new TaskHistory { TaskId = t.Id, At = now, Action = "Khôi phục", Detail = "Khôi phục từ thùng rác" });
        }
        await db.SaveChangesAsync();
        RaiseChanged();
    }

    /// <summary>Xóa vĩnh viễn các công việc đang nằm trong thùng rác.</summary>
    public async Task PurgeAsync(IReadOnlyCollection<int> ids)
    {
        if (ids.Count == 0) return;
        await using var db = await _factory.CreateDbContextAsync();
        await using var tx = await db.Database.BeginTransactionAsync();
        var rows = await db.Tasks.Where(t => ids.Contains(t.Id) && t.IsDeleted).Select(t => new { t.Id, t.SyncId }).ToListAsync();
        var valid = rows.Select(r => r.Id).ToList();
        // Dấu vết xóa để các máy đồng bộ cũng xóa vĩnh viễn
        foreach (var r in rows)
        {
            var tomb = await db.SyncTombstones.FirstOrDefaultAsync(x => x.Kind == "task" && x.SyncId == r.SyncId);
            if (tomb == null) db.SyncTombstones.Add(tomb = new SyncTombstone { Kind = "task", SyncId = r.SyncId });
            tomb.SyncStamp = AppDbContext.NextStamp();
            tomb.SyncDirty = true;
        }
        await db.SaveChangesAsync();
        await db.TaskHistories.Where(h => valid.Contains(h.TaskId)).ExecuteDeleteAsync();
        await db.ReminderLogs.Where(r => valid.Contains(r.TaskId)).ExecuteDeleteAsync();
        await db.TaskDependencies.Where(d => valid.Contains(d.TaskId) || valid.Contains(d.DependsOnTaskId)).ExecuteDeleteAsync();
        await db.Attachments.Where(a => a.TaskId != null && valid.Contains(a.TaskId.Value)).ExecuteUpdateAsync(s => s.SetProperty(a => a.TaskId, (int?)null));
        await db.Notifications.Where(n => n.TaskId != null && valid.Contains(n.TaskId.Value)).ExecuteDeleteAsync();
        await db.Tasks.Where(t => t.ParentId != null && valid.Contains(t.ParentId.Value)).ExecuteUpdateAsync(s => s.SetProperty(t => t.ParentId, (int?)null));
        await db.Tasks.Where(t => valid.Contains(t.Id)).ExecuteDeleteAsync();
        await tx.CommitAsync();
        RaiseChanged();
    }

    public async Task<int> CountTrashAsync()
    {
        await using var db = await _factory.CreateDbContextAsync();
        return await db.Tasks.CountAsync(t => t.IsDeleted);
    }

    // ------------------------------------------------------------------ Lịch sử thay đổi

    private static List<string> Diff(WorkTask a, WorkTask b, IReadOnlyDictionary<int, string> categoryNames)
    {
        var list = new List<string>();
        void Cmp<T>(string label, T x, T y, Func<T, string>? fmt = null)
        {
            if (EqualityComparer<T>.Default.Equals(x, y)) return;
            fmt ??= v => v?.ToString() ?? "";
            var fx = fmt(x); var fy = fmt(y);
            if (string.IsNullOrWhiteSpace(fx) && string.IsNullOrWhiteSpace(fy)) return;
            list.Add($"{label}: \"{Short(fx)}\" → \"{Short(fy)}\"");
        }
        static string Short(string s) => s.Length > 80 ? s[..77] + "..." : s;
        static string Norm(string? s) => string.IsNullOrWhiteSpace(s) ? "" : s.Trim();

        Cmp("Nội dung", Norm(a.Title), Norm(b.Title));
        Cmp("Ngày giờ nhận", a.ReceivedAt, b.ReceivedAt, Labels.DateTime);
        Cmp("Hạn hoàn thành", a.DueAt, b.DueAt, Labels.DateTime);
        Cmp("Ưu tiên", a.Priority, b.Priority, Labels.Of);
        Cmp("Trạng thái", a.Status, b.Status, Labels.Of);
        Cmp("Nhóm công việc", a.CategoryId, b.CategoryId, v => v.HasValue && categoryNames.TryGetValue(v.Value, out var n) ? n : "(trống)");
        Cmp("Đề tài/dự án", Norm(a.ProjectName), Norm(b.ProjectName));
        Cmp("Người giao", Norm(a.AssignedBy), Norm(b.AssignedBy));
        Cmp("Người thực hiện", Norm(a.Assignee), Norm(b.Assignee));
        Cmp("Đơn vị phối hợp", Norm(a.Coordinator), Norm(b.Coordinator));
        Cmp("Thời lượng dự kiến (giờ)", a.EstimatedHours, b.EstimatedHours, v => v?.ToString("0.##") ?? "");
        Cmp("Bắt đầu theo kế hoạch", a.PlannedStart, b.PlannedStart, Labels.DateTime);
        Cmp("Thời điểm cố định", a.IsFixedTime, b.IsFixedTime, v => v ? "Có" : "Không");
        Cmp("Tiến độ (%)", a.Progress, b.Progress);
        Cmp("Kết quả bàn giao", Norm(a.Deliverable), Norm(b.Deliverable));
        Cmp("Tình trạng hồ sơ", a.DocumentStatus, b.DocumentStatus, Labels.Of);
        Cmp("Số văn bản", Norm(a.DocumentNumber), Norm(b.DocumentNumber));
        Cmp("Xin ý kiến lãnh đạo", Norm(a.LeaderOpinionNeeded), Norm(b.LeaderOpinionNeeded));
        Cmp("Ghi chú", Norm(a.Notes), Norm(b.Notes));
        Cmp("Thư mục hồ sơ", Norm(a.FolderPath), Norm(b.FolderPath));
        Cmp("Dự kiến phản hồi", a.ExpectedResponseAt, b.ExpectedResponseAt, Labels.DateTime);
        Cmp("Trọng số", a.Weight, b.Weight > 0 ? b.Weight : 1, v => v.ToString("0.##"));
        Cmp("Dự án (mã nội bộ)", a.ProjectId, b.ProjectId, v => v?.ToString() ?? "(trống)");
        Cmp("Công việc cha (mã nội bộ)", a.ParentId, b.ParentId, v => v?.ToString() ?? "(trống)");
        return list;
    }
}
