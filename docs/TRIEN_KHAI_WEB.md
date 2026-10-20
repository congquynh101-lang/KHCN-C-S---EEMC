# HƯỚNG DẪN ĐƯA EEMC WORK MANAGER LÊN WEB VÀ CẬP NHẬT LIÊN TỤC

Phiên bản web 2.0. Dùng bằng trình duyệt trên máy tính và điện thoại. Dữ liệu nằm trên máy chủ nên mọi thiết bị luôn thấy cùng một dữ liệu mới nhất.

> **Cách nhanh nhất (không cần IIS, không cần cài .NET):** dùng gói `BanCaiDat\EEMC-WorkManager-Web-2.0.0-CaiDat.zip`. Giải nén trên một máy luôn bật, chạy `CaiDat-Web.bat` (quyền quản trị), là phần mềm chạy nền như dịch vụ Windows ở cổng 5080. Máy khác trong mạng nội bộ mở `http://<tên máy>:5080`. Gói này hợp để bắt đầu dùng ngay trong mạng nội bộ; dùng lâu dài nên chuyển sang IIS có HTTPS như các phần dưới đây.

Hướng dẫn gồm 7 phần. Làm lần lượt; phần nào chỉ làm **một lần** được ghi rõ.

| Phần | Nội dung | Ai làm | Bao lâu |
|---|---|---|---|
| A | Chạy thử ngay trên máy của bạn | Bạn | 5 phút |
| B | Đưa mã nguồn lên GitHub (kho riêng tư) | Bạn | 20 phút, một lần |
| C | Chuẩn bị máy chủ IIS nội bộ | Bạn + bộ phận CNTT | 1–2 giờ, một lần |
| D | Triển khai lần đầu | Bạn hoặc CNTT | 15 phút |
| E | **Cập nhật liên tục tự động (CI/CD)** | Bạn + CNTT | 30 phút, một lần |
| F | Vận hành: sao lưu, khôi phục, quên mật khẩu | Quản trị | — |
| G | Phương án thay thế: cloud (Azure) | — | — |

---

## A. Chạy thử ngay trên máy của bạn

1. Mở PowerShell trong thư mục dự án `Documents\EEMC-WorkManager`.
2. Chạy:

```bash
powershell -ExecutionPolicy Bypass -File deploy\run-local.ps1
```

3. Trình duyệt tự mở `http://localhost:5080`. Lần đầu sẽ hiện trang **Thiết lập lần đầu** để tạo tài khoản quản trị (mật khẩu tối thiểu 10 ký tự, có cả chữ và số).
4. Dữ liệu chạy thử nằm ở `%LOCALAPPDATA%\EEMC\WorkManagerWeb`. Thư mục này cố ý nằm ngoài OneDrive: không nên để CSDL SQLite trong thư mục đang được OneDrive đồng bộ.
5. Đóng cửa sổ PowerShell hoặc nhấn Ctrl+C để dừng.

> Chế độ này chỉ để thử nghiệm. Máy khác chưa vào được vì máy bạn không phải máy chủ, và máy cá nhân có thể tắt bất cứ lúc nào.

---

## B. Đưa mã nguồn lên GitHub (làm một lần)

Mục đích: lưu mã nguồn an toàn, theo dõi lịch sử thay đổi, và làm nguồn cho cập nhật tự động.

1. **Cài Git** cho Windows: tải từ https://git-scm.com/download/win, cài với tùy chọn mặc định. Máy này hiện chưa có Git.
2. **Tạo tài khoản GitHub** (nếu chưa có) tại https://github.com. Nên dùng email công ty và bật xác thực 2 lớp.
3. Tạo kho mới: nút **New repository** → đặt tên `eemc-workmanager` → chọn **Private** (bắt buộc: đây là phần mềm nội bộ) → không tích thêm README → **Create repository**.
4. Mở PowerShell trong thư mục dự án và chạy lần lượt từng lệnh (thay `<tai-khoan>` bằng tên tài khoản GitHub của bạn):

```bash
git init -b main
```

```bash
git add .
```

```bash
git commit -m "EEMC Work Manager 2.0 - ban web va giai doan 2"
```

```bash
git remote add origin https://github.com/<tai-khoan>/eemc-workmanager.git
```

```bash
git push -u origin main
```

Lần đẩy đầu tiên, Git sẽ mở cửa sổ đăng nhập GitHub.

5. Tệp `.gitignore` đã cấu hình sẵn để **không** đưa lên GitHub: dữ liệu (`*.db`, `App_Data`), khóa mã hóa cookie (`keys`), cấu hình riêng của máy chủ (`appsettings.Production.json`) và thư mục build.

> Nếu EEMC dùng Azure DevOps hoặc GitLab nội bộ thay cho GitHub, các bước tương tự; tệp quy trình CI/CD ở phần E cần chuyển sang định dạng của hệ thống đó.

---

## C. Chuẩn bị máy chủ IIS nội bộ (làm một lần, cần bộ phận CNTT)

**Đề xuất cấu hình:** Windows Server 2019/2022, 2 CPU, 4 GB RAM, ổ dữ liệu riêng (ví dụ `D:`), nằm trong mạng nội bộ EEMC. Truy cập từ xa (điện thoại, công tác) đi qua VPN của công ty. Không nên mở thẳng ra Internet.

1. **Bật IIS:** Server Manager → Add Roles and Features → Web Server (IIS), giữ các thành phần mặc định. Tích thêm **WebSocket Protocol**, vì giao diện web cần WebSocket để cập nhật tức thì.
2. **Cài ASP.NET Core Hosting Bundle 8.0:** tải tại https://dotnet.microsoft.com/download/dotnet/8.0 (mục *Hosting Bundle*). Cài xong thì khởi động lại IIS bằng lệnh `iisreset`.
3. **Chép thư mục `deploy`** của dự án lên máy chủ, ví dụ vào `C:\EEMC-Deploy\deploy`.
4. Mở **PowerShell (Run as Administrator)** trên máy chủ và chạy:

```bash
powershell -ExecutionPolicy Bypass -File C:\EEMC-Deploy\deploy\setup-iis-server.ps1 -SiteDir C:\inetpub\eemc-workmanager -DataDir D:\EEMC-WorkManager-Data -HostName congviec.eemc.local
```

Script này sẽ:
- tạo website và Application Pool `EEMC-WorkManager`, cấu hình *AlwaysRunning* để nhắc việc chạy liên tục kể cả khi không ai mở trình duyệt;
- tạo thư mục dữ liệu và cấp quyền ghi cho Application Pool;
- tạo `appsettings.Production.json` trỏ tới thư mục dữ liệu. Tệp này không bao giờ bị ghi đè khi cập nhật.

5. **Tên miền nội bộ:** nhờ CNTT tạo bản ghi DNS `congviec.eemc.local` (hoặc tên khác) trỏ về IP máy chủ.
6. **HTTPS (bắt buộc trước khi dùng thật):** nhờ CNTT cấp chứng chỉ (từ CA nội bộ của công ty, hoặc chứng chỉ thương mại nếu truy cập qua Internet). Trong IIS Manager → Site → **Bindings** → Add → `https`, cổng 443 → chọn chứng chỉ. Phần mềm tự chuyển http sang https khi đã có binding https.
7. **Sao lưu máy chủ:** đưa thư mục `D:\EEMC-WorkManager-Data` vào lịch sao lưu của CNTT. Thư mục này chứa CSDL, tệp đính kèm, khóa cookie, nhật ký và các bản sao lưu tự động hằng ngày.

---

## D. Triển khai lần đầu

1. Trên máy phát triển, tạo gói cài đặt (gói này tự chạy toàn bộ kiểm thử trước khi đóng gói):

```bash
powershell -ExecutionPolicy Bypass -File deploy\publish-web.ps1
```

Kết quả nằm ở `artifacts\EEMC-WorkManager-Web-<phiên bản>.zip`.

2. Chép tệp `.zip` lên máy chủ, rồi chạy trên máy chủ với quyền Administrator:

```bash
powershell -ExecutionPolicy Bypass -File C:\EEMC-Deploy\deploy\deploy-iis.ps1 -Package D:\Releases\EEMC-WorkManager-Web-2.0.0.zip
```

3. Mở `https://congviec.eemc.local` → tạo tài khoản quản trị ở trang **Thiết lập lần đầu**.
4. **Chuyển dữ liệu từ bản desktop:** trên máy đang dùng bản desktop, vào Cài đặt → *Sao lưu ngay*. Trên web, vào **Cài đặt → Nhập dữ liệu từ bản desktop** → chọn tệp `EEMC-WM-....db`. Có thể nhập nhiều lần mà không bị nhân đôi; trước mỗi lần nhập, hệ thống tự sao lưu dữ liệu web.
5. Trên điện thoại: mở địa chỉ web → menu trình duyệt → **Thêm vào màn hình chính** để dùng như một ứng dụng.

**Script `deploy-iis.ps1` làm gì mỗi lần cập nhật:**
1. Sao lưu CSDL (`...-truoc-cap-nhat.db`).
2. Hiện trang "đang cập nhật" (`app_offline.htm`) cho người dùng.
3. Giữ lại bản cũ để có thể quay lại (mặc định giữ 5 bản gần nhất).
4. Chép bản mới, không đụng tới dữ liệu và cấu hình riêng của máy chủ.
5. Chạy lại và kiểm tra địa chỉ `/health`.
6. **Nếu kiểm tra thất bại, tự quay lại bản cũ.**

Khi khởi động, CSDL được tự nâng cấp lên cấu trúc mới (cơ chế migration), không cần chạy lệnh nào thêm.

---

## E. Cập nhật liên tục tự động (CI/CD)

Dự án đã có sẵn 2 quy trình trong thư mục `.github/workflows`:

| Tệp | Chạy khi | Làm gì |
|---|---|---|
| `ci.yml` | Mỗi lần đẩy mã lên `main`/`develop` hoặc tạo Pull Request | Build, chạy toàn bộ kiểm thử, đóng gói bản web và bản desktop (.exe) để tải về |
| `deploy-iis.yml` | Mỗi lần đẩy mã lên `main` | Kiểm thử → đóng gói → **tự cập nhật máy chủ IIS** bằng `deploy-iis.ps1` |

**Vì sao máy chủ nội bộ vẫn nhận được cập nhật?** Bước triển khai chạy trên một *self-hosted runner*: chương trình nhỏ của GitHub cài trên chính máy chủ IIS. Runner **tự kết nối ra** GitHub để nhận việc, nên không cần mở cổng nào từ Internet vào mạng EEMC.

### E1. Cài runner trên máy chủ IIS (làm một lần)

1. Trên GitHub: kho `eemc-workmanager` → **Settings → Actions → Runners → New self-hosted runner** → chọn **Windows**.
2. Làm theo các lệnh GitHub hiển thị: tải về, giải nén vào `C:\actions-runner`, chạy `config.cmd`.
   - Khi được hỏi *labels*, nhập: `eemc-iis`
   - Khi được hỏi chạy như dịch vụ (*run as service*), chọn **Y**. Dịch vụ cần quyền Administrator để dừng và chạy IIS: cấu hình tài khoản dịch vụ theo hướng dẫn của CNTT.
3. Cho phép máy chủ truy cập ra ngoài `github.com` và `*.actions.githubusercontent.com` (cổng 443).

### E2. Bật cổng duyệt trước khi cập nhật (khuyến nghị)

GitHub → **Settings → Environments → New environment** → tên `production` → tích **Required reviewers** → chọn chính bạn. Từ nay, mỗi lần có bản mới, GitHub sẽ chờ bạn bấm **Approve** rồi mới cập nhật máy chủ.

### E3. Biến cấu hình (nếu khác mặc định)

GitHub → **Settings → Secrets and variables → Actions → Variables**:

| Biến | Mặc định |
|---|---|
| `EEMC_SITE_DIR` | `C:\inetpub\eemc-workmanager` |
| `EEMC_DATA_DIR` | `D:\EEMC-WorkManager-Data` |
| `EEMC_APP_POOL` | `EEMC-WorkManager` |
| `EEMC_HEALTH_URL` | `http://localhost/health` |

### E4. Quy trình cập nhật hằng ngày từ đây

1. Sửa mã (hoặc nhờ trợ lý AI sửa) trên máy phát triển.
2. Chạy thử bằng `deploy\run-local.ps1`.
3. Đẩy mã lên GitHub:

```bash
git add .
```

```bash
git commit -m "Mo ta ngan thay doi"
```

```bash
git push
```

4. GitHub tự kiểm thử. Nếu có kiểm thử thất bại thì **dừng**, máy chủ không bị đụng tới.
5. (Nếu đã bật E2) bấm **Approve** trên GitHub.
6. Máy chủ tự cập nhật trong khoảng 1–2 phút. Người dùng chỉ thấy trang "đang cập nhật" trong vài giây.

Theo dõi tiến trình tại tab **Actions** của kho. Bản desktop `.exe` mới nhất của mỗi lần build có sẵn ở mục *Artifacts* của lần chạy `ci.yml`.

**Đánh số phiên bản:** bản web lấy số `2.0.<số lần chạy>`, hiển thị tại Cài đặt → Phiên bản. Muốn lên phiên bản lớn, sửa `<Version>` trong `src/EEMC.WM.Web/EEMC.WM.Web.csproj` và số `2.0` trong các tệp workflow.

---

## F. Vận hành

| Việc | Cách làm |
|---|---|
| Sao lưu tự động | Mỗi ngày một bản tại `<DataDir>\SaoLuu`, mặc định giữ 30 bản. Tải về được tại Cài đặt → Sao lưu |
| Sao lưu thủ công | Cài đặt → **Sao lưu ngay** |
| Phục hồi | Dừng Application Pool → chép tệp sao lưu `.db` đè lên `<DataDir>\workmanager.db` (xóa `-wal`, `-shm` nếu có) → chạy lại Application Pool |
| Quên mật khẩu | Trên máy chủ: `dotnet C:\inetpub\eemc-workmanager\EEMC.WM.Web.dll --EEMC:DataDir=D:\EEMC-WorkManager-Data --reset-password <tên đăng nhập> <mật khẩu mới>` |
| Tài khoản bị khóa | Tự mở sau 15 phút (khóa khi nhập sai mật khẩu 5 lần liên tiếp), hoặc đặt lại mật khẩu như trên |
| Xem lỗi | `<DataDir>\logs\yyyy-MM-dd.log` |
| Kiểm tra sống | Mở `/health`, trả về `Healthy` |
| Tệp đính kèm | `<DataDir>\files\năm\tháng\...`, tối đa 50 MB mỗi tệp; chỉ nhận các loại tài liệu văn phòng, ảnh, nén |

**Bảo mật đã có:**
- Bắt buộc đăng nhập cho mọi trang.
- Mật khẩu được băm theo chuẩn PBKDF2 của ASP.NET Core Identity.
- Khóa tài khoản tạm thời khi nhập sai nhiều lần.
- Cookie HttpOnly, chống giả mạo yêu cầu (antiforgery).
- Chặn đọc tệp ngoài thư mục lưu trữ.
- Gửi kèm các tiêu đề bảo mật cơ bản.

**Cần CNTT bổ sung:** HTTPS (phần C6) và giới hạn truy cập trong mạng nội bộ/VPN.

---

## G. Phương án thay thế: cloud (Azure App Service)

Dùng khi EEMC muốn truy cập qua Internet mà không có máy chủ nội bộ. Cần xin phép về bảo mật, vì dữ liệu công việc sẽ nằm ngoài mạng EEMC.

1. Tạo **App Service** (Windows, .NET 8), gói từ B1 trở lên. Bật *Always On* và *Web sockets*.
2. Configuration → Application settings: `EEMC__DataDir` = `C:\home\data\eemc` (thư mục `home` được lưu bền vững).
3. Triển khai: thay job `deploy` trong `deploy-iis.yml` bằng action `azure/webapps-deploy@v3` kèm *publish profile* lưu ở GitHub Secrets.
4. Lưu ý: SQLite phù hợp khi chạy **1 phiên bản (instance)**. Nếu sau này mở rộng nhiều người dùng hoặc nhiều instance, nên chuyển sang Azure SQL. Lớp dữ liệu dùng EF Core nên chỉ cần đổi provider.

---

## Kiến trúc tóm tắt

```
Trình duyệt (máy tính / điện thoại)
        │  HTTPS + WebSocket (Blazor Server)
        ▼
IIS ── EEMC.WM.Web (ASP.NET Core 8)
        ├─ Đăng nhập cookie, giao diện tiếng Việt, sáng/tối
        ├─ Dịch vụ nền: nhắc việc mỗi phút, sao lưu mỗi ngày
        └─ EEMC.WM.Core + EEMC.WM.Data (dùng chung với bản desktop)
                └─ SQLite: <DataDir>\workmanager.db  +  files\  +  SaoLuu\  +  logs\
```
