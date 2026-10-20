using EEMC.WM.Core.Domain;
using Microsoft.EntityFrameworkCore;

namespace EEMC.WM.Data.Services;

/// <summary>Danh mục nghiệp vụ có thể chỉnh sửa, bổ sung.</summary>
public sealed class CategoryService
{
    private readonly IDbContextFactory<AppDbContext> _factory;

    public CategoryService(IDbContextFactory<AppDbContext> factory) { _factory = factory; }

    public event EventHandler? Changed;

    /// <summary>Tất cả danh mục đang dùng, sắp theo nhóm rồi mục con.</summary>
    public async Task<List<Category>> GetAllAsync(bool includeInactive = false)
    {
        await using var db = await _factory.CreateDbContextAsync();
        var all = await db.Categories.AsNoTracking().Include(c => c.Parent).ToListAsync();
        if (!includeInactive) all = all.Where(c => c.IsActive && (c.Parent == null || c.Parent.IsActive)).ToList();
        var groups = all.Where(c => c.ParentId == null).OrderBy(c => c.SortOrder).ThenBy(c => c.Name).ToList();
        var result = new List<Category>();
        foreach (var g in groups)
        {
            result.Add(g);
            result.AddRange(all.Where(c => c.ParentId == g.Id).OrderBy(c => c.SortOrder).ThenBy(c => c.Name));
        }
        return result;
    }

    public async Task<Category> AddAsync(string name, int? parentId)
    {
        name = (name ?? "").Trim();
        if (name.Length == 0) throw new ValidationException(new[] { "Tên nhóm/mục không được để trống." });
        await using var db = await _factory.CreateDbContextAsync();
        if (await db.Categories.AnyAsync(c => c.ParentId == parentId && c.Name == name && c.IsActive))
            throw new ValidationException(new[] { "Tên này đã tồn tại." });
        var order = await db.Categories.Where(c => c.ParentId == parentId).Select(c => (int?)c.SortOrder).MaxAsync() ?? -1;
        var c = new Category { Name = name, ParentId = parentId, SortOrder = order + 1 };
        db.Categories.Add(c);
        await db.SaveChangesAsync();
        Changed?.Invoke(this, EventArgs.Empty);
        return c;
    }

    public async Task RenameAsync(int id, string name)
    {
        name = (name ?? "").Trim();
        if (name.Length == 0) throw new ValidationException(new[] { "Tên nhóm/mục không được để trống." });
        await using var db = await _factory.CreateDbContextAsync();
        var c = await db.Categories.FirstOrDefaultAsync(x => x.Id == id) ?? throw new InvalidOperationException("Không tìm thấy danh mục.");
        c.Name = name;
        await db.SaveChangesAsync();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Ngừng sử dụng danh mục. Công việc đã gắn danh mục vẫn giữ nguyên liên kết.</summary>
    public async Task DeactivateAsync(int id)
    {
        await using var db = await _factory.CreateDbContextAsync();
        var c = await db.Categories.FirstOrDefaultAsync(x => x.Id == id) ?? throw new InvalidOperationException("Không tìm thấy danh mục.");
        c.IsActive = false;
        await db.SaveChangesAsync();
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
