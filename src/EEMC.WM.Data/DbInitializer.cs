using EEMC.WM.Core.Domain;
using EEMC.WM.Data.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace EEMC.WM.Data;

/// <summary>Áp dụng migration và khởi tạo danh mục nghiệp vụ mặc định (chỉ danh mục, không tạo dữ liệu công việc).</summary>
public static class DbInitializer
{
    public static async Task InitializeAsync(IDbContextFactory<AppDbContext> factory)
    {
        await using var db = await factory.CreateDbContextAsync();
        await db.Database.MigrateAsync();
        // WAL: an toàn hơn khi ứng dụng đóng bất thường, đọc/ghi đồng thời tốt hơn
        await db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;");
        await db.Database.ExecuteSqlRawAsync("PRAGMA synchronous=NORMAL;");

        // Danh mục mặc định có sẵn trên mọi máy: dùng định danh cố định, không cần gửi đi khi đồng bộ
        db.ImportMode = true;
        if (!await db.Categories.AnyAsync())
        {
            int order = 0;
            foreach (var (group, items) in DefaultCatalog)
            {
                var g = new Category { Name = group, SortOrder = order++, SyncId = CategorySyncId(group, null), SyncStamp = 1 };
                db.Categories.Add(g);
                await db.SaveChangesAsync();
                int i = 0;
                foreach (var item in items)
                    db.Categories.Add(new Category { Name = item, ParentId = g.Id, SortOrder = i++, SyncId = CategorySyncId(group, item), SyncStamp = 1 });
                await db.SaveChangesAsync();
            }
            AppLog.Info("Đã khởi tạo danh mục nghiệp vụ mặc định.");
        }
        else if (!await db.Settings.AnyAsync(s => s.Key == CategoryIdsMarker))
        {
            // Nâng cấp từ bản 1.0: đổi định danh danh mục mặc định sang định danh cố định
            var all = await db.Categories.ToListAsync();
            foreach (var c in all)
            {
                var parent = c.ParentId.HasValue ? all.FirstOrDefault(p => p.Id == c.ParentId) : null;
                var group = parent?.Name ?? c.Name;
                var item = parent == null ? null : c.Name;
                bool isDefault = DefaultCatalog.Any(d => d.Group == group && (item == null || d.Items.Contains(item)));
                if (!isDefault) continue;
                var id = CategorySyncId(group, item);
                if (all.Any(x => x != c && x.SyncId == id)) continue;
                c.SyncId = id;
                c.SyncStamp = 1;
                c.SyncDirty = false;
            }
            await db.SaveChangesAsync();
        }
        if (!await db.Settings.AnyAsync(s => s.Key == CategoryIdsMarker))
        {
            db.Settings.Add(new AppSetting { Key = CategoryIdsMarker, Value = "1" });
            await db.SaveChangesAsync();
        }
        db.ImportMode = false;
    }

    private const string CategoryIdsMarker = "sync-category-ids-v1";

    public static string CategorySyncId(string group, string? item) =>
        SyncIds.Deterministic("eemc-category|" + group + "|" + (item ?? ""));

    public static readonly (string Group, string[] Items)[] DefaultCatalog =
    {
        ("Quản lý nhiệm vụ KH&CN", new[]
        {
            "Đề xuất nhiệm vụ", "Lập thuyết minh", "Lập dự toán", "Chuẩn bị hồ sơ tuyển chọn/giao trực tiếp",
            "Ký hợp đồng", "Theo dõi tiến độ", "Báo cáo định kỳ", "Nghiệm thu cấp cơ sở", "Nghiệm thu cấp trên",
            "Thanh quyết toán", "Đăng ký, lưu trữ kết quả nghiên cứu"
        }),
        ("Nghiên cứu và phát triển sản phẩm mới", new[]
        {
            "Máy biến áp 110–500 kV", "Kháng điện 500 kV", "Máy biến dòng điện CT", "Máy biến điện áp VT/CVT",
            "Recloser và LBS", "Trạm biến áp di động", "Các sản phẩm công nghệ mới khác"
        }),
        ("Doanh nghiệp KH&CN", new[]
        {
            "Hồ sơ chứng nhận doanh nghiệp KH&CN", "Theo dõi điều kiện duy trì", "Hồ sơ sản phẩm KH&CN",
            "Báo cáo hoạt động R&D", "Hồ sơ ưu đãi, hỗ trợ", "Làm việc với cơ quan quản lý nhà nước"
        }),
        ("Sáng kiến và đổi mới sáng tạo", new[]
        {
            "Tiếp nhận sáng kiến", "Thẩm tra sáng kiến", "Tổ chức họp Hội đồng sáng kiến", "Tổng hợp biên bản",
            "Trình phê duyệt", "Ban hành quyết định", "Theo dõi giá trị làm lợi", "Tổng hợp báo cáo sáng kiến"
        }),
        ("Hợp tác và chuyển giao công nghệ", new[]
        {
            "Thỏa thuận hợp tác", "Hợp đồng tư vấn", "Chuyển giao công nghệ", "Hợp tác với trường đại học, viện nghiên cứu",
            "Hợp tác với chuyên gia trong và ngoài nước", "Đào tạo và tiếp nhận công nghệ"
        }),
        ("Hội đồng khoa học", new[]
        {
            "Chuẩn bị tài liệu", "Gửi giấy mời", "Lập chương trình họp", "Tổng hợp ý kiến", "Lập biên bản",
            "Theo dõi kết luận", "Đôn đốc hoàn thiện hồ sơ"
        }),
        ("Công tác hành chính KH&CN", new[]
        {
            "Soạn công văn", "Soạn tờ trình", "Soạn báo cáo", "Chuẩn bị tài liệu lãnh đạo", "Rà soát văn bản pháp luật",
            "Theo dõi văn bản đến/đi", "Theo dõi chỉ đạo của lãnh đạo", "Lưu trữ hồ sơ"
        }),
        ("Báo cáo và kế hoạch", new[]
        {
            "Báo cáo tuần", "Báo cáo tháng", "Báo cáo quý", "Báo cáo năm", "Kế hoạch KH&CN", "Kế hoạch R&D",
            "Kế hoạch đổi mới sáng tạo", "Báo cáo đột xuất"
        }),
    };
}
