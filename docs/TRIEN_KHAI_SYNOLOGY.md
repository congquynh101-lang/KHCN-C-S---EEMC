# Chạy EEMC Work Manager Web trên NAS Synology

Phần mềm chạy trong **Container Manager** (Docker) của Synology: nhẹ hơn nhiều so với tạo máy ảo Windows,
không cần bản quyền Windows, tự khởi động cùng NAS và cập nhật bằng cách tải lại ảnh mới.

```
Bạn sửa mã ──► GitHub (nhánh main) ──► GitHub Actions đóng gói ảnh Docker ──► ghcr.io
                                                                              │
                         Máy tính / điện thoại trong mạng ◄── NAS Synology ◄──┘ (tải ảnh, chạy container)
```

## 0. Yêu cầu

| Hạng mục | Yêu cầu |
|---|---|
| DSM | 7.2 trở lên, đã cài gói **Container Manager** (Trung tâm gói) |
| Dòng NAS | Chip Intel/AMD (x86-64) hoặc ARM 64-bit. Ảnh được đóng gói cho cả hai loại |
| Bộ nhớ | Còn trống khoảng 300 MB RAM; dữ liệu ban đầu dưới 50 MB |
| Mạng | NAS có địa chỉ IP cố định trong mạng nội bộ (ví dụ `192.168.1.20`) |

> Nếu Trung tâm gói không có Container Manager, dòng NAS của bạn không hỗ trợ Docker — xem mục 10 (máy ảo).

## 1. Một lần trên GitHub: cho phép NAS tải ảnh

Mỗi lần mã mới vào nhánh `main`, workflow **"Ảnh Docker (Synology / Linux)"** tự đóng gói ảnh
`ghcr.io/congquynh101-lang/eemc-workmanager-web`. Lần đầu GitHub để ảnh ở chế độ riêng tư, cần mở công khai
để NAS tải về mà không phải đăng nhập:

1. Mở trang hồ sơ GitHub của bạn → tab **Packages** → **eemc-workmanager-web**.
2. **Package settings** (cuối cột phải) → mục *Danger Zone* → **Change visibility** → **Public** → gõ tên gói để xác nhận.

Ảnh chỉ chứa chương trình, **không chứa dữ liệu** hay mật khẩu nào. Mã nguồn vốn đã công khai.

<details><summary>Muốn giữ ảnh ở chế độ riêng tư?</summary>

Tạo *Personal access token (classic)* chỉ có quyền `read:packages` (GitHub → Settings → Developer settings).
Trên NAS: Container Manager → **Sổ đăng ký** → **Cài đặt** → **Thêm** → địa chỉ `https://ghcr.io`,
tên người dùng GitHub và token vừa tạo. Bạn tự nhập token trên NAS, không gửi cho người khác.
</details>

## 2. Tạo thư mục cho phần mềm

**File Station** → thư mục chia sẻ `docker` (Container Manager tạo sẵn) → **Tạo** → **Tạo thư mục** → `eemc-workmanager`.

Toàn bộ dữ liệu sẽ nằm trong `/docker/eemc-workmanager/data`.

## 3. Tạo dự án trong Container Manager

1. **Container Manager** → **Dự án** → **Tạo**.
2. Điền:
   - **Tên dự án**: `eemc-workmanager`
   - **Đường dẫn**: chọn `/docker/eemc-workmanager`
   - **Nguồn**: *Tạo docker-compose.yml*
3. Dán nội dung tệp [`deploy/synology/docker-compose.yml`](../deploy/synology/docker-compose.yml):

   ```yaml
   services:
     eemc-workmanager:
       image: ghcr.io/congquynh101-lang/eemc-workmanager-web:latest
       container_name: eemc-workmanager
       restart: unless-stopped
       ports:
         - "8088:8080"
       environment:
         TZ: Asia/Ho_Chi_Minh
       volumes:
         - ./data:/data
   ```

4. **Tiếp** → bỏ qua mục *Cổng thông tin web (Web Station)* → **Tiếp** → **Hoàn tất**.
   Container Manager tải ảnh (chiếm khoảng 370 MB sau khi tải) rồi khởi động. Trạng thái chuyển sang **Đang chạy**,
   khoảng 1 phút sau cột tình trạng hiện **Khỏe mạnh** (healthy).

Nếu cổng `8088` đã có ứng dụng khác dùng, đổi số bên trái, ví dụ `"8090:8080"`.

## 4. Mở phần mềm và tạo tài khoản

Trên máy tính cùng mạng, mở `http://<IP-NAS>:8088` (ví dụ `http://192.168.1.20:8088`).
Lần đầu hiện trang **Thiết lập lần đầu**: tạo tài khoản quản trị (mật khẩu từ 10 ký tự, có chữ và số).
Trang này chỉ dùng được một lần — hãy tạo tài khoản ngay sau khi cài.

## 5. Bật HTTPS bằng Reverse Proxy của DSM (khuyến nghị)

Mật khẩu đăng nhập đi qua mạng; dùng HTTPS để mã hóa.

1. **Bảng điều khiển** → **Cổng đăng nhập** → **Nâng cao** → **Proxy ngược** → **Tạo**.
2. **Nguồn**: Giao thức `HTTPS`, Tên máy chủ `*` (hoặc tên miền nội bộ nếu có), Cổng `8443`.
3. **Đích**: Giao thức `HTTP`, Tên máy chủ `localhost`, Cổng `8088`.
4. Tab **Tiêu đề tùy chỉnh** → **Tạo** → chọn **WebSocket**. *Bắt buộc* — thiếu bước này trang mở được
   nhưng các nút bấm không phản hồi (phần mềm dùng kết nối WebSocket).
5. **Lưu**. Mở `https://<IP-NAS>:8443`.
6. Chứng chỉ: **Bảng điều khiển** → **Bảo mật** → **Chứng chỉ**. Với IP nội bộ, trình duyệt sẽ cảnh báo chứng chỉ
   tự ký; nhờ phòng CNTT cấp chứng chỉ nội bộ hoặc dùng tên miền có chứng chỉ hợp lệ.

## 6. Bảo mật mạng

- Chỉ dùng trong mạng nội bộ EEMC. **Không** mở cổng 8088/8443 ra Internet trên router.
- Cần dùng từ xa: kết nối **VPN Server** của Synology (hoặc VPN công ty) rồi mở địa chỉ nội bộ như bình thường.
- **Bảng điều khiển** → **Bảo mật** → **Tường lửa**: cho phép cổng 8088/8443 chỉ với dải IP nội bộ.
- Phần mềm tự khóa tài khoản 15 phút sau 5 lần nhập sai mật khẩu.

## 7. Sao lưu

Thư mục `/docker/eemc-workmanager/data` chứa:

| Thành phần | Nội dung |
|---|---|
| `workmanager.db` | Cơ sở dữ liệu công việc |
| `files/` | Tài liệu đính kèm |
| `SaoLuu/` | Bản sao lưu phần mềm tự tạo mỗi ngày (mặc định giữ 30 bản, chỉnh trong Cài đặt) |
| `keys/` | Khóa phiên đăng nhập — mất thì chỉ phải đăng nhập lại |
| `logs/` | Nhật ký hoạt động |

Tạo tác vụ **Hyper Backup** sao lưu thư mục `docker/eemc-workmanager` sang ổ USB hoặc NAS khác.
Khi khôi phục, ưu tiên tệp trong `SaoLuu/` (luôn nhất quán) hơn bản `workmanager.db` đang mở.

## 8. Cập nhật phiên bản mới

1. Đẩy mã mới vào nhánh `main` (ví dụ sửa trong Codespaces rồi `git push`).
2. Chờ workflow **"Ảnh Docker (Synology / Linux)"** trên tab **Actions** xong (khoảng 5–10 phút, dấu ✓ xanh).
3. Trên NAS, một trong hai cách (tên nút có thể khác đôi chút theo phiên bản Container Manager):
   - **Container Manager** → **Hình ảnh** → ảnh `eemc-workmanager-web` hiện nhãn **Có bản cập nhật** → bấm **Cập nhật**; hoặc
   - **Dự án** → `eemc-workmanager` → **Dừng** → **Hành động** → **Dọn dẹp** → **Build** (tải ảnh `latest` mới, tạo lại container).

   Dữ liệu trong `data/` giữ nguyên; phần mềm tự nâng cấp cơ sở dữ liệu khi khởi động.

Mỗi bản đóng gói còn có nhãn theo mã commit (ví dụ `:e3b80d5`). Muốn quay lại bản cũ, sửa dòng `image:` thành
nhãn đó rồi **Build** lại. Nên sao chép `data/SaoLuu` ra ngoài trước khi cập nhật lớn.

## 9. Việc thường gặp

**Quên mật khẩu**: **Container Manager** → **Container** → `eemc-workmanager` → **Hành động** → **Mở terminal**
→ **Tạo** → `bash`, rồi gõ:

```bash
dotnet EEMC.WM.Web.dll --reset-password <tên đăng nhập> <mật khẩu mới>
```

**Chuyển dữ liệu từ bản cài trên Windows**: dừng dự án, chép `workmanager.db` (và thư mục `files`) từ
`C:\ProgramData\EEMC\WorkManagerWeb` vào `/docker/eemc-workmanager/data`, rồi khởi động lại.
Dữ liệu từ bản desktop: dùng **Cài đặt → Nhập dữ liệu từ bản desktop** trong phần mềm.

| Hiện tượng | Cách xử lý |
|---|---|
| Container dừng ngay sau khi chạy | Container → `eemc-workmanager` → tab **Nhật ký** để xem lỗi |
| Không tải được ảnh (*denied*/*unauthorized*) | Ảnh còn riêng tư — làm mục 1 |
| Báo cổng đã dùng | Đổi `8088` trong docker-compose rồi **Build** lại |
| Trang mở được nhưng bấm không phản hồi | Thiếu tiêu đề WebSocket ở Proxy ngược (mục 5) |
| Giờ nhắc việc lệch | Kiểm tra `TZ: Asia/Ho_Chi_Minh` và giờ hệ thống của NAS |

## 10. Phương án khác: máy ảo Windows (Virtual Machine Manager)

Chỉ dùng khi NAS không có Container Manager nhưng có **Virtual Machine Manager** và đủ RAM (từ 4 GB cho máy ảo).
Cài Windows Server/Windows 10 trong máy ảo (cần bản quyền), rồi dùng bộ cài
`EEMC-WorkManager-Web-2.0.0-CaiDat.zip` → chạy `CaiDat-Web.bat` như trên máy chủ Windows thông thường.
Phương án này nặng hơn và phải tự cập nhật Windows.
