# Kiến trúc và cơ sở dữ liệu

## Công nghệ

| Thành phần | Lựa chọn |
|---|---|
| Ngôn ngữ / nền tảng | C# 12, .NET 8 LTS |
| Giao diện | WPF, MVVM (CommunityToolkit.Mvvm 8.2), giao diện tự thiết kế, không phụ thuộc thư viện UI trả phí |
| Dependency Injection | Microsoft.Extensions.DependencyInjection |
| Cơ sở dữ liệu | SQLite (chế độ WAL), Entity Framework Core 8 có migration |
| Excel | ClosedXML (MIT) |
| Thông báo | NotifyIcon của Windows (hiện dạng toast trên Windows 10/11) |
| Kiểm thử | xUnit |

Không dùng DevExpress vì chưa có giấy phép. Các thành phần đều miễn phí và chạy hoàn toàn offline.

## Cấu trúc solution

```
EEMC.WorkManager.sln
├─ src/EEMC.WM.Core     Nghiệp vụ thuần, không phụ thuộc giao diện hay CSDL
│   ├─ Domain/          Thực thể WorkTask, Category, TaskHistory, AppNotification...; enum trạng thái, ưu tiên
│   ├─ Services/        PriorityEngine (ưu tiên), ScheduleEngine (lập lịch), WorkCalendar (giờ làm việc),
│   │                   ReminderPlanner (quyết định nhắc việc), TaskValidator (kiểm tra dữ liệu)
│   ├─ Settings/        WorkSettings (giờ làm việc, ngày nghỉ, mốc nhắc...)
│   ├─ Text/            Nhãn tiếng Việt, định dạng dd/MM/yyyy HH:mm, chuẩn hóa tìm kiếm không dấu
│   └─ Time/            IClock, VietnamClock (Asia/Ho_Chi_Minh)
├─ src/EEMC.WM.Data     Truy cập dữ liệu
│   ├─ AppDbContext, Migrations/, DbInitializer (migrate + danh mục mặc định)
│   └─ Services/        TaskService, CategoryService, SettingsService, NotificationService,
│                       ReminderService, DashboardService, BackupService, ExcelExportService
├─ src/EEMC.WM.App      WPF (MVVM)
│   ├─ ViewModels/      Main, Dashboard, Today, TaskList, Calendar, Documents, Settings, TaskEditor
│   ├─ Views/           XAML tương ứng
│   ├─ Controls/        DateTimeInput (ô nhập ngày + giờ)
│   ├─ Infrastructure/  TaskActions, DialogService, ThemeManager, TrayService, ReminderHost, Converters
│   └─ Themes/          Light.xaml, Dark.xaml (màu), Styles.xaml, Templates.xaml
├─ tests/EEMC.WM.Tests  49 kiểm thử: ưu tiên, lập lịch, nhắc việc, dịch vụ dữ liệu, 10.000 công việc, sao lưu/phục hồi
└─ tools/EEMC.WM.DevSeed  Công cụ tạo dữ liệu mô phỏng cho KIỂM THỬ, chỉ ghi vào thư mục riêng
                          (từ chối ghi vào thư mục dữ liệu thật)
```

Hướng mở rộng: tích hợp Outlook, Teams hay Google Calendar sẽ là một dịch vụ mới đọc `TaskService` và `ScheduleEngine`. Muốn chạy nhiều người dùng thì thay chuỗi kết nối hoặc provider của EF Core. Tầng Core không phải sửa.

## Cơ sở dữ liệu (SQLite)

Tệp dữ liệu: `%LOCALAPPDATA%\EEMC\WorkManager\workmanager.db`. Có thể đổi thư mục bằng biến môi trường `EEMC_WM_DATA`. Lược đồ được tạo và nâng cấp tự động bằng EF Core migration (`Migrations/…_InitialCreate`) mỗi khi khởi động.

### WorkTasks — Công việc

| Cột | Kiểu | Ghi chú |
|---|---|---|
| Id | INTEGER PK | |
| Code | TEXT(32) UNIQUE | `CV-yyyy-nnnnn`, tự sinh |
| ReceivedAt, DueAt, Title, Priority | | **4 trường bắt buộc** |
| Status | INTEGER | 1..9 (Chưa thực hiện … Hủy) |
| CategoryId | INTEGER FK → Categories | null = chưa phân nhóm |
| ProjectName, AssignedBy, Assignee, Coordinator | TEXT | |
| EstimatedHours | REAL null | null = chưa ước tính |
| PlannedStart, IsFixedTime | | Lịch kế hoạch / lịch cố định |
| Progress | INTEGER | 0–100 |
| Deliverable, DocumentStatus, DocumentNumber, LeaderOpinionNeeded, Notes, FolderPath | | |
| ExpectedResponseAt | | Ngày dự kiến phản hồi (dùng để đôn đốc) |
| ParentId | INTEGER null | Dành cho công việc con (Giai đoạn 2) |
| StartedAt, CompletedAt, ClosedAt, CreatedAt, UpdatedAt | | |
| IsDeleted, DeletedAt | | Thùng rác |
| SnoozeUntil | | Tạm hoãn nhắc việc |
| SearchText | TEXT | Chuẩn hóa không dấu, chữ thường, dùng cho tìm kiếm |

Chỉ mục: (IsDeleted, Status, DueAt), DueAt, Priority, CategoryId, PlannedStart, ReceivedAt, ClosedAt, ParentId.

### Các bảng khác

| Bảng | Nội dung |
|---|---|
| Categories | Danh mục nghiệp vụ 2 cấp (ParentId), SortOrder, IsActive. Khởi tạo 8 nhóm theo yêu cầu, người dùng chỉnh sửa được |
| TaskHistories | Lịch sử thay đổi (giá trị cũ → mới) và lịch sử trao đổi, xử lý (IsComment) |
| Notifications | Trung tâm thông báo (Title, Message, Severity, IsRead) |
| ReminderLogs | (TaskId, ReminderKey) UNIQUE — các mốc đã nhắc, để không nhắc trùng |
| Settings | Key/Value, lưu cấu hình người dùng dạng JSON |
| CodeCounters | Bộ đếm sinh mã công việc theo năm |

## Hiệu năng

Lọc, đếm và phân trang chạy trực tiếp trong SQLite, có chỉ mục. Riêng sắp xếp theo "thứ tự đề xuất" tính điểm trong bộ nhớ trên tập đã lọc. Kiểm thử `Paging_handles_10000_tasks` chạy với 10.000 công việc: truy vấn trang, sắp xếp đề xuất và toàn bộ số liệu tổng quan hoàn tất trong giới hạn 10 giây đặt cho bài kiểm thử. DataGrid dùng ảo hóa dòng.

## Build từ mã nguồn

```bash
dotnet build EEMC.WorkManager.sln -c Release
```

```bash
dotnet test tests/EEMC.WM.Tests
```

```bash
dotnet publish src/EEMC.WM.App -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o publish/EEMC-WorkManager-win-x64
```

Tạo migration mới khi thay đổi mô hình dữ liệu:

```bash
dotnet ef migrations add TenMigration --project src/EEMC.WM.Data --startup-project src/EEMC.WM.Data
```

Mở `EEMC.WorkManager.sln` bằng Visual Studio 2022 (17.8 trở lên), đặt `EEMC.WM.App` làm Startup Project.

Chạy thử với dữ liệu mô phỏng mà không ảnh hưởng dữ liệu thật:

```bash
dotnet run --project tools/EEMC.WM.DevSeed -- D:\EEMC-Test 200
```

Sau đó đặt `EEMC_WM_DATA=D:\EEMC-Test` rồi chạy phần mềm.
