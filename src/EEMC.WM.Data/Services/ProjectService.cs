using EEMC.WM.Data.Infrastructure;
using EEMC.WM.Core.Domain;
using EEMC.WM.Core.Services;
using EEMC.WM.Core.Time;
using Microsoft.EntityFrameworkCore;

namespace EEMC.WM.Data.Services;

public sealed class ProjectSummary
{
    public required Project Project { get; init; }
    public int TaskCount { get; init; }
    public int OpenCount { get; init; }
    public int OverdueCount { get; init; }
    public int CompletedCount { get; init; }
    /// <summary>Tiến độ tổng thể tính từ công việc (có trọng số); null khi chưa có công việc.</summary>
    public int? Progress { get; init; }
    public Milestone? NextMilestone { get; init; }
}

/// <summary>Mẫu nghiệp vụ: chỉ gợi ý tên và các mốc điển hình, KHÔNG có ngày, tiến độ hay trạng thái thực tế.</summary>
public sealed record ProjectTemplate(string Name, string ManagementLevel, string Description, string[] Milestones);

/// <summary>Quản lý đề tài/dự án KH&amp;CN.</summary>
public sealed class ProjectService
{
    private readonly IDbContextFactory<AppDbContext> _factory;
    private readonly IClock _clock;

    public ProjectService(IDbContextFactory<AppDbContext> factory, IClock clock)
    {
        _factory = factory;
        _clock = clock;
    }

    public event EventHandler? Changed;

    public static readonly IReadOnlyList<string> ManagementLevels = new[]
    {
        "Cấp Nhà nước", "Cấp Bộ", "Cấp Tỉnh/Thành phố", "Cấp Tập đoàn", "Cấp Tổng công ty", "Cấp cơ sở"
    };

    private static readonly string[] StandardSteps =
    {
        "Đề xuất nhiệm vụ", "Thuyết minh, dự toán được phê duyệt", "Ký hợp đồng thực hiện", "Báo cáo tiến độ giữa kỳ",
        "Chế tạo, thử nghiệm mẫu", "Nghiệm thu cấp cơ sở", "Nghiệm thu cấp trên", "Thanh quyết toán", "Đăng ký, lưu trữ kết quả"
    };

    public static readonly IReadOnlyList<ProjectTemplate> Templates = new[]
    {
        new ProjectTemplate("Nghiên cứu chế tạo kháng điện 500 kV", "Cấp Tổng công ty",
            "Nghiên cứu, thiết kế, chế tạo và thử nghiệm kháng điện 500 kV.", StandardSteps),
        new ProjectTemplate("Phát triển CT/CVT 110–220 kV", "Cấp Tổng công ty",
            "Phát triển máy biến dòng điện CT và máy biến điện áp kiểu tụ CVT cấp điện áp 110–220 kV.", StandardSteps),
        new ProjectTemplate("Nghiên cứu Recloser/LBS 22–35 kV", "Cấp Tổng công ty",
            "Nghiên cứu, chế tạo thiết bị đóng cắt Recloser và LBS 22–35 kV.", StandardSteps),
        new ProjectTemplate("Nghiên cứu trạm biến áp di động 40 MVA", "Cấp Tổng công ty",
            "Nghiên cứu, thiết kế trạm biến áp di động công suất 40 MVA.", StandardSteps),
        new ProjectTemplate("Quản lý hồ sơ doanh nghiệp KH&CN", "Cấp cơ sở",
            "Theo dõi hồ sơ chứng nhận và điều kiện duy trì doanh nghiệp khoa học và công nghệ.",
            new[] { "Rà soát căn cứ pháp lý", "Thu thập tài liệu từ các đơn vị", "Kiểm tra điều kiện", "Hoàn thiện phương án SXKD",
                    "Trình lãnh đạo phê duyệt", "Nộp hồ sơ", "Theo dõi phản hồi cơ quan quản lý", "Bổ sung hồ sơ (nếu có)" }),
    };

    public async Task<List<ProjectSummary>> GetSummariesAsync(bool includeArchived = false)
    {
        await using var db = await _factory.CreateDbContextAsync();
        var projects = await db.Projects.AsNoTracking().Include(p => p.Milestones)
            .Where(p => includeArchived || !p.IsArchived).OrderByDescending(p => p.UpdatedAt).ToListAsync();
        var ids = projects.Select(p => p.Id).ToList();
        var tasks = await db.Tasks.AsNoTracking().Where(t => t.ProjectId != null && ids.Contains(t.ProjectId.Value) && !t.IsDeleted)
            .Select(t => new WorkTask { Id = t.Id, ProjectId = t.ProjectId, ParentId = t.ParentId, Status = t.Status, Progress = t.Progress, Weight = t.Weight, DueAt = t.DueAt })
            .ToListAsync();
        var now = _clock.Now;
        return projects.Select(p =>
        {
            var pt = tasks.Where(t => t.ProjectId == p.Id).ToList();
            return new ProjectSummary
            {
                Project = p,
                TaskCount = pt.Count,
                OpenCount = pt.Count(t => !StatusGroups.IsClosed(t.Status)),
                OverdueCount = pt.Count(t => !StatusGroups.IsClosed(t.Status) && t.DueAt < now),
                CompletedCount = pt.Count(t => t.Status == WorkTaskStatus.Completed),
                Progress = ProgressCalculator.Weighted(pt.Where(t => t.ParentId == null || pt.All(x => x.Id != t.ParentId))),
                NextMilestone = p.Milestones.Where(m => m.CompletedAt == null).OrderBy(m => m.DueDate ?? DateTime.MaxValue).ThenBy(m => m.SortOrder).FirstOrDefault()
            };
        }).ToList();
    }

    public async Task<Project?> GetAsync(int id)
    {
        await using var db = await _factory.CreateDbContextAsync();
        var p = await db.Projects.AsNoTracking().Include(x => x.Milestones).FirstOrDefaultAsync(x => x.Id == id);
        if (p != null) p.Milestones = p.Milestones.OrderBy(m => m.SortOrder).ThenBy(m => m.DueDate).ToList();
        return p;
    }

    public async Task<List<Project>> GetActiveListAsync()
    {
        await using var db = await _factory.CreateDbContextAsync();
        return await db.Projects.AsNoTracking().Where(p => !p.IsArchived).OrderBy(p => p.Code).ToListAsync();
    }

    public async Task<List<WorkTask>> GetTasksAsync(int projectId)
    {
        await using var db = await _factory.CreateDbContextAsync();
        var list = await db.Tasks.AsNoTracking().Where(t => t.ProjectId == projectId && !t.IsDeleted)
            .OrderBy(t => t.ParentId ?? t.Id).ThenBy(t => t.ParentId == null ? 0 : 1).ThenBy(t => t.DueAt).ToListAsync();
        await TaskService.FillDependenciesAsync(db, list);
        return list;
    }

    public async Task<Project> SaveAsync(Project input)
    {
        Access.Require(UserRole.Editor);
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(input.Name)) errors.Add("Vui lòng nhập tên đề tài/dự án.");
        if (input.StartDate.HasValue && input.EndDate.HasValue && input.EndDate < input.StartDate)
            errors.Add("Ngày kết thúc không được trước ngày bắt đầu.");
        if (errors.Count > 0) throw new ValidationException(errors);

        await using var db = await _factory.CreateDbContextAsync();
        var now = _clock.Now;
        Project p;
        if (input.Id == 0)
        {
            p = new Project { CreatedAt = now };
            db.Projects.Add(p);
            p.Code = string.IsNullOrWhiteSpace(input.Code) ? await NextCodeAsync(db) : input.Code.Trim();
        }
        else
        {
            p = await db.Projects.FirstOrDefaultAsync(x => x.Id == input.Id) ?? throw new InvalidOperationException("Không tìm thấy dự án.");
            if (!string.IsNullOrWhiteSpace(input.Code)) p.Code = input.Code.Trim();
        }
        p.Name = input.Name.Trim();
        p.ManagementLevel = Clean(input.ManagementLevel);
        p.LeadUnit = Clean(input.LeadUnit);
        p.Leader = Clean(input.Leader);
        p.StartDate = input.StartDate;
        p.EndDate = input.EndDate;
        p.Status = input.Status;
        p.Description = Clean(input.Description);
        p.Issues = Clean(input.Issues);
        p.DecisionsNeeded = Clean(input.DecisionsNeeded);
        p.FolderPath = Clean(input.FolderPath);
        p.IsArchived = input.IsArchived;
        p.UpdatedAt = now;
        await db.SaveChangesAsync();
        Changed?.Invoke(this, EventArgs.Empty);
        return p;
    }

    /// <summary>Tạo dự án từ mẫu: tên, mô tả, cấp quản lý và các mốc (không có ngày).</summary>
    public async Task<Project> CreateFromTemplateAsync(ProjectTemplate tpl)
    {
        Access.Require(UserRole.Editor);
        var p = await SaveAsync(new Project { Name = tpl.Name, ManagementLevel = tpl.ManagementLevel, Description = tpl.Description });
        await using var db = await _factory.CreateDbContextAsync();
        int i = 0;
        foreach (var m in tpl.Milestones) db.Milestones.Add(new Milestone { ProjectId = p.Id, Name = m, SortOrder = i++ });
        await db.SaveChangesAsync();
        return p;
    }

    private async Task<string> NextCodeAsync(AppDbContext db)
    {
        var prefix = $"DA-{_clock.Now.Year}-";
        var codes = await db.Projects.Where(p => p.Code.StartsWith(prefix)).Select(p => p.Code).ToListAsync();
        var max = codes.Select(c => int.TryParse(c.AsSpan(prefix.Length), out var n) ? n : 0).DefaultIfEmpty(0).Max();
        return $"{prefix}{max + 1:000}";
    }

    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    public async Task DeleteAsync(int id)
    {
        Access.Require(UserRole.Admin);
        await using var db = await _factory.CreateDbContextAsync();
        await db.Tasks.Where(t => t.ProjectId == id).ExecuteUpdateAsync(s => s.SetProperty(t => t.ProjectId, (int?)null));
        var code = await db.Projects.Where(p => p.Id == id).Select(p => p.Code).FirstOrDefaultAsync() ?? "Dự án";
        await DocumentService.DetachOwnerAsync(db, new DocOwner(ProjectId: id), code, _clock.Now);
        await db.Projects.Where(p => p.Id == id).ExecuteDeleteAsync();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    // ---------------- Mốc

    public async Task<Milestone> SaveMilestoneAsync(Milestone input)
    {
        Access.Require(UserRole.Editor);
        if (string.IsNullOrWhiteSpace(input.Name)) throw new ValidationException(new[] { "Vui lòng nhập tên mốc." });
        await using var db = await _factory.CreateDbContextAsync();
        Milestone m;
        if (input.Id == 0)
        {
            var order = await db.Milestones.Where(x => x.ProjectId == input.ProjectId).Select(x => (int?)x.SortOrder).MaxAsync() ?? -1;
            m = new Milestone { ProjectId = input.ProjectId, SortOrder = order + 1 };
            db.Milestones.Add(m);
        }
        else m = await db.Milestones.FirstOrDefaultAsync(x => x.Id == input.Id) ?? throw new InvalidOperationException("Không tìm thấy mốc.");
        m.Name = input.Name.Trim();
        m.DueDate = input.DueDate;
        m.CompletedAt = input.CompletedAt;
        m.Notes = Clean(input.Notes);
        await db.SaveChangesAsync();
        await db.Projects.Where(p => p.Id == m.ProjectId).ExecuteUpdateAsync(s => s.SetProperty(p => p.UpdatedAt, _clock.Now));
        Changed?.Invoke(this, EventArgs.Empty);
        return m;
    }

    public async Task DeleteMilestoneAsync(int id)
    {
        Access.Require(UserRole.Editor);
        await using var db = await _factory.CreateDbContextAsync();
        await db.Milestones.Where(m => m.Id == id).ExecuteDeleteAsync();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Gắn các công việc cũ có "Tên đề tài/dự án" trùng tên dự án vào dự án (dữ liệu từ Giai đoạn 1).</summary>
    public async Task<int> LinkByProjectNameAsync(int projectId)
    {
        Access.Require(UserRole.Editor);
        await using var db = await _factory.CreateDbContextAsync();
        var p = await db.Projects.AsNoTracking().FirstAsync(x => x.Id == projectId);
        var key = Core.Text.TextNormalizer.Normalize(p.Name);
        var candidates = await db.Tasks.Where(t => t.ProjectId == null && t.ProjectName != null).ToListAsync();
        int n = 0;
        foreach (var t in candidates.Where(t => Core.Text.TextNormalizer.Normalize(t.ProjectName) == key))
        {
            t.ProjectId = projectId;
            n++;
        }
        await db.SaveChangesAsync();
        return n;
    }
}
