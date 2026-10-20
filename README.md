# EEMC KHCN WORK MANAGER

Trợ lý quản lý công việc Khoa học, Công nghệ và Đổi mới sáng tạo — công tác thư ký KH&CN, Tổng công ty Thiết bị điện Đông Anh – CTCP (EEMC).

| Bản | Dùng khi | Bắt đầu |
|---|---|---|
| **Web 2.0** (khuyến nghị) | Dùng trên mọi máy và điện thoại, dữ liệu luôn cập nhật, đầy đủ Giai đoạn 1 + 2 | [docs/TRIEN_KHAI_WEB.md](docs/TRIEN_KHAI_WEB.md) — chạy thử ngay: `deploy\run-local.ps1` |
| Web 2.0 trên NAS Synology | Chạy thường trực trên NAS của đơn vị (Container Manager / Docker) | [docs/TRIEN_KHAI_SYNOLOGY.md](docs/TRIEN_KHAI_SYNOLOGY.md) |
| Desktop 1.1 | Máy Windows không có mạng | `publish\EEMC-WorkManager-1.1.0` — [docs/HUONG_DAN_SU_DUNG.md](docs/HUONG_DAN_SU_DUNG.md) |

- Tính năng đã có / còn hạn chế: [docs/DANH_SACH_TINH_NANG.md](docs/DANH_SACH_TINH_NANG.md)
- Kiến trúc, cơ sở dữ liệu, cách build: [docs/KIEN_TRUC_VA_CSDL.md](docs/KIEN_TRUC_VA_CSDL.md)

## Cấu trúc mã nguồn

```
src/EEMC.WM.Core    Nghiệp vụ: ưu tiên, lập lịch, nhắc việc, tiến độ (dùng chung)
src/EEMC.WM.Data    CSDL SQLite/EF Core, dịch vụ, báo cáo Word/Excel/PDF, đồng bộ (dùng chung)
src/EEMC.WM.Web     Bản web (ASP.NET Core 8 Blazor Server)
src/EEMC.WM.App     Bản desktop (WPF)
tests/              71 kiểm thử tự động
deploy/             Script chạy thử, đóng gói, chuẩn bị máy chủ IIS, cập nhật có quay lại; deploy/synology: docker-compose
Dockerfile          Ảnh Docker (amd64 + arm64) cho Synology / máy chủ Linux
.github/workflows/  CI (kiểm thử + đóng gói), CD (tự cập nhật máy chủ IIS), đóng gói ảnh Docker lên ghcr.io
```

```bash
dotnet test tests/EEMC.WM.Tests
```
