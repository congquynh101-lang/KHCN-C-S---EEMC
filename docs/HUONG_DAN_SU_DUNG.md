# EEMC KHCN WORK MANAGER — HƯỚNG DẪN CÀI ĐẶT VÀ SỬ DỤNG

Phiên bản 1.1.0 (Giai đoạn 1 + đồng bộ nhiều máy, giao diện mới) — Trợ lý quản lý công việc KH&CN và ĐMST, Tổng công ty Thiết bị điện Đông Anh – CTCP.

## 1. Cài đặt

**Yêu cầu:** Windows 10 hoặc 11, bản 64-bit. Không cần Internet. Không cần cài thêm .NET.

1. Chép tệp `EEMC-WorkManager-1.1.0-Setup.zip` sang máy cần cài, rồi **giải nén toàn bộ** (nhấp phải → *Extract All*).
2. Mở thư mục vừa giải nén, nhấp đúp **`CaiDat.bat`**. Không cần quyền quản trị.
   - Phần mềm được cài vào `%LOCALAPPDATA%\Programs\EEMC WorkManager`.
   - Trình cài tạo lối tắt trên Desktop và Start Menu, rồi hỏi có bật **tự chạy khi mở máy** không (nên chọn Có để không bỏ lỡ nhắc việc).
   - Phần mềm xuất hiện trong *Settings → Apps* của Windows.
3. Nếu Windows SmartScreen cảnh báo vì tệp chưa ký số: chọn *More info → Run anyway*. Nếu máy có chính sách bảo mật của đơn vị, nhờ bộ phận CNTT cho phép tệp này.
4. **Cập nhật phiên bản mới:** chạy `CaiDat.bat` của bản mới. Nếu phần mềm đang mở, trình cài sẽ tự đóng nó. Dữ liệu được giữ nguyên.
5. **Gỡ cài đặt:** Start Menu → *Go cai dat EEMC KHCN Work Manager*, hoặc gỡ từ *Settings → Apps*. Mặc định dữ liệu công việc được giữ lại; muốn xóa cả dữ liệu thì gõ `XOA` khi được hỏi.

Cũng có thể chạy không cần cài đặt: nhấp đúp thẳng `EEMC.WorkManager.exe`.

Dữ liệu được lưu tại `%LOCALAPPDATA%\EEMC\WorkManager\workmanager.db`. Bản sao lưu tự động nằm ở `Documents\EEMC WorkManager\SaoLuu`. Nhật ký lỗi nằm ở `%LOCALAPPDATA%\EEMC\WorkManager\logs`.

Mỗi lúc chỉ chạy được một cửa sổ phần mềm. Nếu mở thêm lần nữa, cửa sổ đang chạy sẽ được đưa lên trước.

## 2. Nhập công việc nhanh

Bấm **＋ Thêm công việc** hoặc nhấn **Ctrl+N**. Chỉ cần điền 4 trường:

| Trường | Cách nhập |
|---|---|
| Nội dung công việc | Con trỏ đặt sẵn ở ô này, gõ ngay |
| Ngày giờ nhận | Mặc định là thời điểm hiện tại, có thể sửa |
| Hạn hoàn thành | Chọn ngày, gõ giờ (`17`, `1730`, `17:30`, `17h30` đều được). Các nút nhanh: *Hôm nay, Ngày mai, +3 ngày làm việc, Thứ Sáu này, Thứ Sáu tuần sau* |
| Mức độ ưu tiên | Khẩn cấp / Cao / Bình thường / Thấp (mặc định Bình thường) |

Nhấn **Ctrl+Enter** để lưu, **Esc** để hủy. Phần mềm tự sinh mã `CV-năm-số thứ tự`, tự tính thời gian còn lại, mức khẩn cấp, góc ma trận Eisenhower, mức ưu tiên đề xuất, rồi đưa công việc vào lịch và nhắc việc.

Mục **Thông tin mở rộng** không bắt buộc: nhóm nghiệp vụ, đề tài/dự án, người giao, người thực hiện, đơn vị phối hợp, thời lượng dự kiến, thời gian bắt đầu theo kế hoạch, thời điểm cố định (họp), tiến độ, kết quả bàn giao, tình trạng hồ sơ, số văn bản, nội dung xin ý kiến lãnh đạo, ghi chú, đường dẫn hồ sơ, ngày dự kiến phản hồi.

> Nên nhập **thời lượng dự kiến** cho việc lớn. Khi chưa có, phần mềm chỉ dùng thời lượng mặc định (2 giờ) để *gợi ý* và luôn ghi rõ là "ước tính". Phần mềm không khẳng định kịp hạn hay không dựa trên số ước tính đó.

## 3. Các màn hình

- **Tổng quan:** 8 chỉ số (bấm vào để mở danh sách đã lọc sẵn), **5 công việc cần ưu tiên xử lý hôm nay** (có nút Bắt đầu, Hoàn thành, Trạng thái), biểu đồ theo trạng thái, theo nhóm nghiệp vụ, nhận/hoàn thành/tồn đọng 8 tuần, tỷ lệ đúng hạn.
- **Công việc hôm nay:** thứ tự xử lý đề xuất kèm lý do; lịch đề xuất trong ngày (bấm *Chấp thuận* để ghi thời gian bắt đầu theo kế hoạch); cảnh báo không đủ thời gian; danh sách việc chờ đơn vị khác cần đôn đốc.
- **Tất cả công việc:** tìm kiếm (không cần gõ dấu), lọc theo trạng thái/ưu tiên/thời gian/nhóm, sắp xếp hai tiêu chí, phân trang, sửa, sao chép, hoàn thành, xóa vào thùng rác, khôi phục, xuất Excel. Nhấp đúp hoặc Enter để mở; phím Delete để xóa.
- **Lịch làm việc:** xem Ngày / Tuần / Tháng. **Kéo thả** một công việc sang ngày khác, phần mềm sẽ hỏi: đổi *hạn hoàn thành* (Yes) hay chỉ đặt *ngày thực hiện theo kế hoạch* (No).
- **Hồ sơ tài liệu:** các công việc có đường dẫn hồ sơ, số văn bản hoặc tình trạng hồ sơ. Tìm theo tên việc, dự án, số văn bản, từ khóa. Nút *Mở hồ sơ* mở tệp/thư mục/đường dẫn mạng bằng ứng dụng mặc định của Windows. Phần mềm không sửa tài liệu gốc.
- **Dự án KH&CN, Báo cáo:** thuộc Giai đoạn 2. Trang này ghi rõ phạm vi sẽ làm.
- **Cài đặt:** giờ làm việc, ngày làm việc, ngày nghỉ, các mốc nhắc, giao diện sáng/tối, sao lưu và phục hồi, danh mục nhóm nghiệp vụ.

## 4. Trạng thái và hoàn thành

Có 9 trạng thái: Chưa thực hiện, Đang thực hiện, Chờ đơn vị phối hợp, Chờ phản hồi, Chờ lãnh đạo phê duyệt, Cần bổ sung hồ sơ, Tạm dừng, Hoàn thành, Hủy.

- Công việc **chỉ được ghi Hoàn thành khi bạn xác nhận**. Hết hạn hoặc tiến độ 100% không làm công việc tự hoàn thành.
- Công việc ở 3 trạng thái *Chờ...* được tách khỏi danh sách đang trực tiếp xử lý, nhưng vẫn được nhắc đôn đốc. Từ ngày dự kiến phản hồi, phần mềm nhắc mỗi ngày một lần.

## 5. Thứ tự ưu tiên đề xuất được tính thế nào

Điểm ưu tiên là tổng các phần sau. Mỗi phần đều hiện ra dưới công việc để bạn thấy lý do.

| Thành phần | Điểm |
|---|---|
| Mức ưu tiên bạn chọn | Khẩn cấp 40 · Cao 30 · Bình thường 20 · Thấp 10 |
| Thời hạn thực tế | Quá hạn 50 (+3 mỗi ngày quá hạn, tối đa +15) · Đến hạn hôm nay 40–45 · Ngày mai 32 · 2–3 ngày 24–28 · Trong 7 ngày 16 · Xa hơn 0–12 |
| Áp lực thời gian (thời lượng cần ÷ giờ làm việc còn lại) | ≥100%: +15 (nếu là số ước tính: +8) · ≥50%: +8 (ước tính: +4) |
| Lịch cố định trong hôm nay | +20 |
| Đang thực hiện dở / Cần bổ sung hồ sơ | +3 / +5 |

Ma trận Eisenhower: *Quan trọng* là mức Cao hoặc Khẩn cấp. *Khẩn cấp* là quá hạn, còn ≤ 2 ngày, hoặc chắc chắn không đủ thời gian.

**Mức ưu tiên đề xuất** chỉ để tham khảo: ≥80 điểm là Khẩn cấp, ≥60 là Cao, ≥35 là Bình thường. Phần mềm không bao giờ tự đổi mức ưu tiên bạn đã đặt.

Ví dụ: Việc C đã quá hạn 2 ngày (ưu tiên Thấp) được 66 điểm. Việc A hạn ngày mai (ưu tiên Cao) được 62 điểm. Việc B hạn sau 7 ngày (ưu tiên Bình thường) được 36 điểm. Thứ tự đề xuất là C, A, B: việc đã quá hạn cần xử lý dứt điểm trước.

## 6. Nhắc việc

- Các mốc mặc định: trước hạn 3 ngày, 1 ngày, 4 giờ, 1 giờ, khi đến hạn. Khi quá hạn, nhắc lại mỗi 24 giờ. Mọi mốc đều chỉnh được trong Cài đặt.
- Nhắc việc hiện qua **thông báo Windows** và **trung tâm thông báo** (biểu tượng chuông).
- Mỗi mốc chỉ nhắc một lần. Nếu phần mềm đã tắt lâu, khi mở lại chỉ nhắc mốc gần hạn nhất của mỗi việc, để không dồn hàng loạt thông báo.
- Có thể **tạm hoãn** một nhắc việc 15 phút, 1 giờ, hoặc đến đầu giờ làm việc ngày hôm sau.
- Khi bấm nút đóng (X), phần mềm thu xuống **khay hệ thống** cạnh đồng hồ và vẫn tiếp tục nhắc. Muốn đóng hẳn: nhấp phải biểu tượng ở khay → *Thoát*.
- Khi bạn đổi hạn hoàn thành, các mốc nhắc của việc đó được tính lại từ đầu.

## 7. Sao lưu và phục hồi

**Tự động:** mỗi ngày, lần đầu mở phần mềm sẽ tạo một bản sao lưu `EEMC-WM-yyyyMMdd-HHmmss-auto.db`. Mặc định giữ 30 bản tự động gần nhất. Bản sao lưu thủ công không bị tự xóa.

**Thủ công:** Cài đặt → *Sao lưu ngay*. Nên đổi thư mục sao lưu sang ổ khác, ổ mạng hoặc OneDrive (Cài đặt → Thư mục sao lưu → Chọn... → **Lưu cài đặt**).

**Phục hồi:**

1. Cài đặt → chọn một bản trong danh sách → *Phục hồi bản đã chọn*. Hoặc chọn *Phục hồi từ tệp...* để lấy tệp `.db` ở nơi khác.
2. Phần mềm kiểm tra tệp có hợp lệ và không bị hỏng không.
3. Phần mềm tự sao lưu dữ liệu hiện tại thành tệp `...-truoc-phuc-hoi.db`, rồi mới ghi đè.
4. Phần mềm tự khởi động lại.

**Chuyển sang máy khác:** sao chép tệp sao lưu `.db` sang máy mới, chạy phần mềm, rồi dùng *Phục hồi từ tệp...*.

Bản sao lưu được tạo bằng cơ chế Online Backup của SQLite, nên vẫn an toàn khi phần mềm đang chạy. Cơ sở dữ liệu chạy ở chế độ WAL và mỗi thao tác được ghi xuống ngay, hạn chế mất dữ liệu khi máy tắt đột ngột.

## 7a. Đồng bộ dữ liệu giữa các máy (từ phiên bản 1.1)

Dùng khi bạn cài phần mềm trên nhiều máy, ví dụ máy văn phòng và laptop, và muốn các máy thấy cùng một danh sách công việc. Đồng bộ đi qua **thư mục OneDrive của EEMC**, không cần máy chủ.

**Thiết lập trên từng máy:**

1. Đăng nhập OneDrive bằng tài khoản EEMC (OneDrive – EEMC) và chờ OneDrive chạy.
2. Vào **Cài đặt → Đồng bộ dữ liệu giữa các máy** → tích **Bật đồng bộ trên máy này**.
3. Bấm **OneDrive EEMC** để tự điền thư mục `OneDrive - EEMC\EEMC WorkManager\DongBo`, hoặc bấm *Chọn...*. **Mọi máy phải dùng cùng một thư mục.**
4. Bấm **Lưu cài đặt**. Phần mềm đồng bộ ngay lần đầu: gửi toàn bộ dữ liệu đang có của máy này và nhận dữ liệu của các máy khác.

**Hoạt động hằng ngày:**

- Phần mềm tự đồng bộ khi mở, sau đó mỗi 2 phút, khoảng 10 giây sau mỗi thay đổi, và khi thoát hẳn. Góc dưới thanh menu trái hiện "Đã đồng bộ hh:mm"; bấm vào đó để đồng bộ ngay.
- Mỗi máy vẫn làm việc **offline** bình thường. Thay đổi được gửi đi khi có mạng và OneDrive đã tải tệp lên.
- Được đồng bộ: công việc, lịch sử trao đổi và xử lý, danh mục nhóm công việc, xóa vào thùng rác và xóa vĩnh viễn, cùng các thiết lập làm việc dùng chung (giờ làm việc, ngày nghỉ, mốc nhắc, thời lượng ước tính).
- **Không** đồng bộ: giao diện sáng/tối, thư mục sao lưu, trạng thái tạm hoãn nhắc việc và đã đọc thông báo. Mỗi máy nhắc việc riêng.
- Nếu cùng một công việc bị sửa trên hai máy trước khi kịp đồng bộ, **bản sửa sau cùng** được giữ. Ghi chú trao đổi thì luôn được cộng dồn, không mất.
- Nếu hai máy đang offline cùng tạo công việc mới và trùng mã `CV-...`, công việc tạo sau tự đổi sang mã mới. Lịch sử của công việc đó ghi lại việc đổi mã.

**Lưu ý:**

- Không sửa hoặc xóa tay các tệp trong thư mục `DongBo`. Mỗi máy có một thư mục con riêng; phần mềm tự dọn tệp cũ.
- Khi đã bật đồng bộ, *Phục hồi* một bản sao lưu cũ chỉ thay dữ liệu trên máy đó. Những thay đổi mới hơn từ các máy khác sẽ được đồng bộ lại về.
- Nếu trước đây bạn đã dùng bản 1.0 trên nhiều máy và **chép cùng một dữ liệu** sang các máy bằng sao lưu/phục hồi, hãy chỉ giữ dữ liệu ở một máy: các máy còn lại cài mới, bật đồng bộ, rồi để dữ liệu tự về. Làm vậy để tránh bị nhân đôi công việc.

## 8. Xóa và thùng rác

*Xóa* sẽ chuyển công việc vào **Thùng rác** (nút ở trang Tất cả công việc). Từ đó có thể *Khôi phục*. *Xóa vĩnh viễn* phải xác nhận và không thể hoàn tác, trừ khi phục hồi từ bản sao lưu.

## 9. Phím tắt

| Phím | Tác dụng |
|---|---|
| Ctrl+N | Thêm công việc |
| Ctrl+Enter, Ctrl+S | Lưu công việc |
| Esc | Đóng cửa sổ nhập |
| Ctrl+F | Tìm kiếm |
| Ctrl+1 / 2 / 3 / 4 / 6 / 8 | Tổng quan / Hôm nay / Tất cả công việc / Lịch / Hồ sơ / Cài đặt |
| Enter (trên danh sách) | Mở công việc |
| Delete (trên danh sách) | Chuyển vào thùng rác |

## 10. Xử lý sự cố

- **Không mở được phần mềm:** xem tệp nhật ký trong `%LOCALAPPDATA%\EEMC\WorkManager\logs`.
- **Không thấy thông báo Windows:** kiểm tra Cài đặt → *Hiển thị thông báo Windows*. Trong Windows, kiểm tra Settings → System → Notifications và chế độ *Do not disturb / Focus*.
- **Lỗi khi xuất Excel:** đóng tệp cùng tên nếu đang mở trong Excel, rồi xuất lại.
