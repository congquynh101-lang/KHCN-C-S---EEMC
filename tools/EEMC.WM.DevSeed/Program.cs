// CÔNG CỤ DÀNH CHO KIỂM THỬ – KHÔNG DÙNG CHO DỮ LIỆU THẬT.
// Tạo dữ liệu công việc mô phỏng vào một THƯ MỤC DỮ LIỆU RIÊNG để thử giao diện/hiệu năng.
// Cách dùng: EEMC.WM.DevSeed <thư-mục-dữ-liệu-thử> [số-công-việc]
// Sau đó chạy phần mềm với biến môi trường EEMC_WM_DATA=<thư-mục-dữ-liệu-thử>.
using EEMC.WM.Core.Domain;
using EEMC.WM.Core.Time;
using EEMC.WM.Data;
using EEMC.WM.Data.Infrastructure;
using EEMC.WM.Data.Services;
using Microsoft.EntityFrameworkCore;

if (args.Length < 1)
{
    Console.WriteLine("Cách dùng: EEMC.WM.DevSeed <thư-mục-dữ-liệu-thử> [số-công-việc]");
    return 1;
}

var dir = Path.GetFullPath(args[0]);
var realDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EEMC", "WorkManager");
if (string.Equals(dir.TrimEnd('\\'), realDir.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
{
    Console.WriteLine("Từ chối: không được ghi dữ liệu mô phỏng vào thư mục dữ liệu thật.");
    return 2;
}
int count = args.Length > 1 && int.TryParse(args[1], out var n) ? n : 40;
var syncIdx = Array.IndexOf(args, "--sync");
string? syncFolder = syncIdx >= 0 && syncIdx + 1 < args.Length ? Path.GetFullPath(args[syncIdx + 1]) : null;

var paths = new AppPaths(dir);
AppDbContext.CurrentDeviceId = new DeviceIdentity(paths).DeviceId;
var factory = new Factory(paths.ConnectionString);
await DbInitializer.InitializeAsync(factory);
var settings = new SettingsService(factory);
await settings.LoadAsync();
if (syncFolder != null)
{
    var s = settings.CloneCurrent();
    s.SyncEnabled = true;
    s.SyncFolder = syncFolder;
    await settings.SaveAsync(s);
    Console.WriteLine($"Đã bật đồng bộ thử với thư mục {syncFolder}");
}
var clock = new VietnamClock();
var tasks = new TaskService(factory, clock, settings);
var cats = await new CategoryService(factory).GetAllAsync();
var leafCats = cats.Where(c => c.ParentId != null).ToList();
var rnd = new Random(7);
var now = clock.Now;

string[] titles =
{
    "[THỬ] Báo cáo tiến độ đề tài kháng điện 500 kV", "[THỬ] Soạn tờ trình phê duyệt thuyết minh nhiệm vụ",
    "[THỬ] Tổng hợp ý kiến Hội đồng khoa học", "[THỬ] Rà soát hồ sơ chứng nhận doanh nghiệp KH&CN",
    "[THỬ] Lập dự toán nhiệm vụ CT/CVT 220 kV", "[THỬ] Gửi giấy mời họp Hội đồng sáng kiến",
    "[THỬ] Theo dõi phản hồi của Sở KH&CN", "[THỬ] Chuẩn bị tài liệu làm việc với chuyên gia",
    "[THỬ] Thẩm tra sáng kiến cải tiến quy trình quấn dây", "[THỬ] Báo cáo hoạt động R&D quý"
};
var statuses = new[] { WorkTaskStatus.NotStarted, WorkTaskStatus.InProgress, WorkTaskStatus.WaitingResponse, WorkTaskStatus.WaitingApproval, WorkTaskStatus.WaitingCoordinator, WorkTaskStatus.NeedMoreDocuments };

for (int i = 0; i < count; i++)
{
    var received = now.AddDays(-rnd.Next(0, 50)).Date.AddHours(8 + rnd.Next(0, 8));
    var due = now.AddHours(rnd.Next(-72, 24 * 20));
    if (due < received) due = received.AddDays(1);
    due = due.Date.AddHours(new[] { 9, 11, 14, 16, 17 }[rnd.Next(5)]);
    if (due < received) due = received.AddHours(4);
    var t = new WorkTask
    {
        Title = titles[i % titles.Length] + (count > titles.Length ? $" #{i + 1}" : ""),
        ReceivedAt = received,
        DueAt = due,
        Priority = (TaskPriority)rnd.Next(1, 5),
        Status = statuses[rnd.Next(statuses.Length)],
        CategoryId = leafCats.Count > 0 ? leafCats[rnd.Next(leafCats.Count)].Id : null,
        EstimatedHours = rnd.Next(3) == 0 ? null : rnd.Next(1, 9),
        Progress = rnd.Next(0, 9) * 10,
        Coordinator = rnd.Next(3) == 0 ? "Phòng Kỹ thuật (thử)" : null,
    };
    if (StatusGroups.IsWaiting(t.Status)) t.ExpectedResponseAt = now.AddDays(rnd.Next(-3, 4));
    if (i % 9 == 0)
    {
        t.IsFixedTime = true;
        t.PlannedStart = now.Date.AddDays(rnd.Next(0, 5)).AddHours(14);
        t.EstimatedHours = 1.5;
        t.Title = "[THỬ] Họp Hội đồng nghiệm thu cấp cơ sở" + (count > titles.Length ? $" #{i + 1}" : "");
    }
    await tasks.CreateAsync(t);
}

// Một ít công việc đã hoàn thành để có số liệu tuần
var open = await tasks.GetOpenTasksAsync();
foreach (var t in count == 0 ? new List<WorkTask>() : open.Take(Math.Max(1, count / 5))) await tasks.ChangeStatusAsync(t.Id, WorkTaskStatus.Completed);

Console.WriteLine($"Đã tạo {count} công việc thử vào {paths.DatabasePath}");
return 0;

sealed class Factory : IDbContextFactory<AppDbContext>
{
    private readonly DbContextOptions<AppDbContext> _o;
    public Factory(string cs) => _o = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(cs).Options;
    public AppDbContext CreateDbContext() => new(_o);
}
