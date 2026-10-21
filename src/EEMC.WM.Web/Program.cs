using System.Globalization;
using EEMC.WM.Core.Time;
using EEMC.WM.Data;
using EEMC.WM.Data.Infrastructure;
using EEMC.WM.Data.Reports;
using EEMC.WM.Data.Services;
using EEMC.WM.Web.Components;
using EEMC.WM.Web.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

var culture = CultureInfo.GetCultureInfo("vi-VN");
CultureInfo.DefaultThreadCurrentCulture = culture;
CultureInfo.DefaultThreadCurrentUICulture = culture;

// Khi chạy dưới dạng dịch vụ Windows (bộ cài CaiDat-Web), thư mục gốc là thư mục chương trình
var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = Microsoft.Extensions.Hosting.WindowsServices.WindowsServiceHelpers.IsWindowsService() ? AppContext.BaseDirectory : null
});
builder.Host.UseWindowsService(o => o.ServiceName = "EEMCWorkManagerWeb");

// ---------------------------------------------------------------- Thư mục dữ liệu
// Ưu tiên: cấu hình EEMC:DataDir → biến môi trường EEMC_WM_DATA → <thư mục chương trình>\App_Data
var dataDir = builder.Configuration["EEMC:DataDir"];
if (string.IsNullOrWhiteSpace(dataDir)) dataDir = Environment.GetEnvironmentVariable("EEMC_WM_DATA");
if (string.IsNullOrWhiteSpace(dataDir)) dataDir = Path.Combine(builder.Environment.ContentRootPath, "App_Data");
var paths = new AppPaths(dataDir);
AppLog.Initialize(paths.LogsDir);

// ---------------------------------------------------------------- Dịch vụ dùng chung với bản desktop
builder.Services.AddSingleton(paths);
builder.Services.AddSingleton<IClock, VietnamClock>();
builder.Services.AddDbContextFactory<AppDbContext>(o => o.UseSqlite(paths.ConnectionString));
builder.Services.AddSingleton<SettingsService>();
builder.Services.AddSingleton<TaskService>();
builder.Services.AddSingleton<CategoryService>();
builder.Services.AddSingleton<NotificationService>();
builder.Services.AddSingleton<ReminderService>();
builder.Services.AddSingleton<DashboardService>();
builder.Services.AddSingleton<BackupService>();
builder.Services.AddSingleton<ExcelExportService>();
builder.Services.AddSingleton<ProjectService>();
builder.Services.AddSingleton<AttachmentService>();
builder.Services.AddSingleton<DocumentService>();
builder.Services.AddSingleton<ChatStore>();
builder.Services.AddSingleton<KnowledgeService>();
builder.Services.AddSingleton<AiSettingsService>();
builder.Services.AddSingleton<ChatService>();
builder.Services.AddHostedService<DocumentIndexWorker>();
builder.Services.AddSingleton<LegalService>();
builder.Services.AddSingleton<UserService>();
builder.Services.AddSingleton<ReportService>();
builder.Services.AddSingleton<DeviceIdentity>();
builder.Services.AddSingleton<SyncService>();
builder.Services.AddSingleton<DesktopImportService>();

// ---------------------------------------------------------------- Dịch vụ web
builder.Services.AddSingleton<NotificationBus>();
builder.Services.AddHostedService<ReminderWorker>();
builder.Services.AddHostedService<BackupWorker>();
builder.Services.AddScoped<UiService>();

builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(paths.DataDir, "keys")))
    .SetApplicationName("EEMC.WorkManager");

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(o =>
    {
        o.LoginPath = "/account/login";
        o.LogoutPath = "/account/logout";
        o.AccessDeniedPath = "/account/login";
        o.Cookie.Name = "EEMC.WM.Auth";
        o.Cookie.HttpOnly = true;
        o.Cookie.SameSite = SameSiteMode.Lax;
        o.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        o.ExpireTimeSpan = TimeSpan.FromHours(12);
        o.SlidingExpiration = true;
        o.Events.OnValidatePrincipal = AccessWiring.ValidatePrincipalAsync;
    });
builder.Services.AddScoped<Microsoft.AspNetCore.Components.Server.Circuits.CircuitHandler, AccessCircuitHandler>();
// Mọi trang/điểm cuối đều cần đăng nhập, trừ trang có [AllowAnonymous]
builder.Services.AddAuthorization(o => o.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());
builder.Services.AddCascadingAuthenticationState();

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents(o => o.DetailedErrors = builder.Environment.IsDevelopment());
builder.Services.AddHealthChecks();

var app = builder.Build();

// ---------------------------------------------------------------- Khởi tạo CSDL
AppDbContext.CurrentDeviceId = app.Services.GetRequiredService<DeviceIdentity>().DeviceId;
await DbInitializer.InitializeAsync(app.Services.GetRequiredService<IDbContextFactory<AppDbContext>>());
await app.Services.GetRequiredService<SettingsService>().LoadAsync();
// Khôi phục mật khẩu trên máy chủ: EEMC.WM.Web.exe --reset-password <tên đăng nhập> <mật khẩu mới>
var resetIdx = Array.IndexOf(args, "--reset-password");
if (resetIdx >= 0)
{
    if (resetIdx + 2 >= args.Length) { Console.WriteLine("Cách dùng: --reset-password <tên đăng nhập> <mật khẩu mới>"); return; }
    try
    {
        await app.Services.GetRequiredService<UserService>().ResetPasswordAsync(args[resetIdx + 1], args[resetIdx + 2]);
        Console.WriteLine("Đã đặt lại mật khẩu.");
    }
    catch (Exception ex) { Console.WriteLine("Không đặt lại được: " + ex.Message); }
    return;
}

// Chỉ dùng khi chạy thử với máy chủ Claude giả lập (không đặt trên máy chủ thật)
if (Environment.GetEnvironmentVariable("EEMC_AI_TEST_BASE_URL") is { Length: > 0 } aiTestUrl)
    app.Services.GetRequiredService<ChatService>().BaseUrlOverride = aiTestUrl;

// Từ đây mọi thao tác ghi dữ liệu phải có người dùng đủ quyền (xem Access, AccessWiring)
Access.Enforce = true;

AppLog.Info($"Khởi động EEMC Work Manager Web {typeof(Program).Assembly.GetName().Version} — dữ liệu: {paths.DataDir}");

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/account/error", createScopeForErrors: true);
    app.UseHsts();
}
app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseAuthentication();
app.Use((ctx, next) => AccessWiring.UseActorAsync(ctx, () => next()));
app.UseAuthorization();
app.UseAntiforgery();

// Tiêu đề bảo mật cơ bản
app.Use(async (ctx, next) =>
{
    ctx.Response.Headers["X-Content-Type-Options"] = "nosniff";
    // SAMEORIGIN: cho phép xem trước PDF/ảnh trong khung của chính trang, vẫn chặn nhúng từ trang khác
    ctx.Response.Headers["X-Frame-Options"] = "SAMEORIGIN";
    ctx.Response.Headers["Referrer-Policy"] = "same-origin";
    await next();
});

app.MapHealthChecks("/health").AllowAnonymous();

app.MapPost("/account/logout", async (HttpContext ctx) =>
{
    await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    return Results.Redirect("/account/login");
});

// Tải tài liệu (đã đăng nhập); v = số phiên bản cũ. Tài liệu trong thùng rác chỉ Biên tập/Quản trị mở được.
app.MapGet("/files/{id:int}", async (int id, bool? inline, int? v, AttachmentService svc) =>
{
    var a = await svc.GetAsync(id);
    if (a?.StoredFile == null || (a.IsDeleted && !Access.Can(EEMC.WM.Core.Domain.UserRole.Editor))) return Results.NotFound();
    var stored = a.StoredFile;
    var name = a.Name;
    if (v is int no && no != a.CurrentVersion)
    {
        var ver = await svc.GetVersionAsync(id, no);
        if (ver == null) return Results.NotFound();
        stored = ver.StoredFile;
        name = ver.FileName;
    }
    var full = svc.ResolveStoredPath(stored);
    if (full == null || !File.Exists(full)) return Results.NotFound();
    // Tên tải về giữ phần mở rộng của tệp thật
    if (Path.GetExtension(name) != Path.GetExtension(full)) name += Path.GetExtension(full);
    return Results.File(File.OpenRead(full), AttachmentService.ContentType(full), inline == true ? null : name, enableRangeProcessing: true);
});

// Tệp gốc văn bản pháp luật (đã đăng nhập)
app.MapGet("/legal-files/{id:int}", async (int id, bool? inline, LegalService svc) =>
{
    var d = await svc.GetAsync(id);
    if (d?.StoredFile == null) return Results.NotFound();
    var full = svc.ResolveStoredPath(d.StoredFile);
    if (full == null || !File.Exists(full)) return Results.NotFound();
    var name = d.FileName ?? Path.GetFileName(full);
    return Results.File(File.OpenRead(full), AttachmentService.ContentType(name), inline == true ? null : name, enableRangeProcessing: true);
});

// Tải bản sao lưu: chỉ quản trị (bản sao lưu chứa toàn bộ dữ liệu và mật khẩu đã băm)
app.MapGet("/backups/{name}", (string name, BackupService backup) =>
{
    var info = backup.ListBackups().FirstOrDefault(b => b.FileName == name);
    return info == null ? Results.NotFound() : Results.File(File.OpenRead(info.Path), "application/octet-stream", info.FileName);
}).RequireAuthorization(p => p.RequireRole(Access.RoleName(EEMC.WM.Core.Domain.UserRole.Admin)));

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();

public partial class Program;
