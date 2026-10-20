# Danh sách tính năng — Web 2.0 (Giai đoạn 1 + 2), Desktop 1.1

## A. Bản web 2.0 (mới) — đã hoàn thành và đã kiểm thử

| Nhóm | Tính năng |
|---|---|
| Nền tảng web | ASP.NET Core 8 Blazor Server, dùng chung tầng nghiệp vụ/dữ liệu với bản desktop. SQLite + EF Core migration (tự nâng cấp CSDL khi cập nhật). Giao diện tiếng Việt, sáng/tối, dùng tốt trên điện thoại. Dữ liệu đổi ở một thiết bị thì các thiết bị đang mở tự cập nhật ngay |
| Đăng nhập | Tài khoản riêng của phần mềm, tạo ở lần chạy đầu. Mật khẩu băm PBKDF2, tối thiểu 10 ký tự. Khóa 15 phút khi sai 5 lần. Đổi mật khẩu trong Cài đặt, đặt lại bằng dòng lệnh trên máy chủ |
| Toàn bộ Giai đoạn 1 | Tổng quan (8 chỉ số, 5 việc ưu tiên, biểu đồ), Công việc hôm nay (thứ tự đề xuất, lịch đề xuất, chấp thuận, cảnh báo, việc chờ đôn đốc), danh sách (tìm không dấu, lọc, sắp xếp, phân trang, chọn nhiều, thùng rác, xuất Excel), thêm/sửa nhanh 4 trường + Ctrl+Enter, phím tắt N và / |
| Lịch | Ngày / Tuần / Tháng, mức tải giờ đề xuất, kéo thả để đổi hạn hoặc đặt ngày thực hiện |
| **Dự án KH&CN** | Mã tự sinh DA-năm-số, cấp quản lý, chủ trì, chủ nhiệm, thời gian, tình trạng. Mốc quan trọng (đánh dấu đạt, quá hạn). Tiến độ tổng thể tính từ công việc có trọng số. Vấn đề tồn đọng, quyết định cần xin ý kiến. 5 mẫu nghiệp vụ (chỉ tên và mốc, không tự tạo ngày hay tiến độ). Gắn công việc cũ theo tên dự án |
| **Công việc con** | Một cấp. Mỗi bước có hạn, trạng thái, người phụ trách, trọng số riêng. Tiến độ công việc cha tự cập nhật nhưng không tự chuyển sang Hoàn thành |
| **Công việc phụ thuộc** | "Phải chờ X hoàn thành trước", chặn phụ thuộc vòng. Việc bị chặn được xếp sau khi tính ưu tiên (−25 điểm, có giải thích). Lịch đề xuất xếp việc phụ thuộc sau khi việc trước kết thúc |
| **Hồ sơ tài liệu** | Tải tệp lên máy chủ (Word, Excel, PDF, PowerPoint, ảnh, ZIP…, tối đa 50 MB), thêm đường dẫn mạng hoặc liên kết. Loại tài liệu tự nhận theo tên (biên bản, quyết định, tờ trình, báo cáo), 6 tình trạng hồ sơ, số văn bản. Tìm kiếm toàn bộ hồ sơ. Không sửa tài liệu gốc |
| **Báo cáo tự động** | Ngày, tuần, tháng đúng các mục trong yêu cầu, chỉ lấy từ dữ liệu đã ghi nhận. Ô nhập khó khăn/kiến nghị do người dùng tự viết. Xuất **Word, Excel, PDF** theo thể thức văn bản hành chính (quốc hiệu, Times New Roman, A4 ngang), in trực tiếp |
| **Kanban** | 7 cột trạng thái, kéo thả để đổi trạng thái (Hoàn thành phải xác nhận), lọc theo dự án |
| **Gantt** | Thanh từ ngày bắt đầu đến hạn, phần đậm thể hiện tiến độ, đường "hôm nay", mốc dự án, khung 30 ngày / 3 tháng / 6 tháng, lọc theo dự án |
| **Ma trận Eisenhower** | 4 ô theo quan trọng × khẩn cấp |
| Nhắc việc | Máy chủ kiểm tra mỗi phút kể cả khi không mở trình duyệt. Trung tâm thông báo, thông báo của Windows/Android qua trình duyệt, tạm hoãn 15 phút / 1 giờ / ngày mai |
| Sao lưu, chuyển dữ liệu | Tự sao lưu hằng ngày, sao lưu ngay, tải bản sao lưu về. Nhập dữ liệu từ bản desktop (nhập lại không bị trùng) |
| Triển khai, cập nhật liên tục | Script chuẩn bị máy chủ IIS, script cập nhật (sao lưu → trang bảo trì → chép → kiểm tra `/health` → tự quay lại bản cũ nếu lỗi). GitHub Actions: CI kiểm thử + đóng gói web và desktop; CD cập nhật máy chủ nội bộ qua self-hosted runner, có cổng duyệt |
| Kiểm thử | **71 test tự động đều đạt**. Ngoài 61 test của Giai đoạn 1 và đồng bộ, có thêm tiến độ có trọng số, công việc con, phụ thuộc (gồm chặn vòng), lập lịch theo phụ thuộc, mẫu dự án không tự tạo tiến độ, tệp đính kèm (chặn đọc tệp ngoài thư mục lưu trữ), đăng nhập và khóa tài khoản, báo cáo + xuất Word/Excel/PDF, nhập dữ liệu desktop. Giao diện web đã chạy thật và kiểm tra từng trang trên trình duyệt, cả khổ điện thoại |

## B. Bản desktop 1.1 (giữ nguyên)

Toàn bộ chức năng Giai đoạn 1, đồng bộ nhiều máy qua OneDrive và giao diện Windows 11 như bản 1.1. Bản desktop **không** có các màn hình Giai đoạn 2. Dữ liệu desktop chuyển lên web bằng chức năng "Nhập dữ liệu từ bản desktop".

## C. Chưa hoàn thành hoặc còn hạn chế

| Hạng mục | Tình trạng |
|---|---|
| Nhập liệu bằng câu tự nhiên ("Ngày 20/10 phải hoàn thành…") | Giai đoạn 3 |
| Phân tích năng suất, cảnh báo nguy cơ trễ hạn nâng cao | Giai đoạn 3 (hiện đã có cảnh báo không đủ thời gian và tỷ lệ đúng hạn) |
| Tích hợp Outlook / Teams / Google Calendar | Giai đoạn 3 |
| Nhiều người dùng, phân quyền theo vai trò | Chưa có. Hiện có thể tạo 1 tài khoản quản trị; CSDL đã có bảng Users để mở rộng |
| Phục hồi bản sao lưu ngay trên giao diện web | Làm trên máy chủ theo hướng dẫn (phần F). Bản desktop có nút phục hồi |
| Đồng bộ trực tiếp bản desktop ↔ web | Chưa có. Hiện chuyển một chiều desktop → web bằng tệp sao lưu |
| Công việc định kỳ, nhập Excel | Chưa có |
| Kéo thả đổi giờ trong lịch ngày | Hiện kéo thả theo ngày |
| Ô chọn ngày giờ | Dùng ô chọn ngày giờ có sẵn của trình duyệt; cách hiển thị (dd/MM hay MM/dd) theo ngôn ngữ cài đặt của trình duyệt |
| HTTPS, tên miền, máy chủ | Cần bộ phận CNTT cấp (hướng dẫn tại `TRIEN_KHAI_WEB.md`) |
| Ký số, bộ cài MSI cho bản desktop | Chưa có |
